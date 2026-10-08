#!/usr/bin/env python3
"""Provision a Linux campaign using manifests; write a reusable source/toolchain lock.

Requires git and Nix with nix-command/flakes enabled. Run outside timed campaigns.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tomllib
from urllib.parse import quote
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
DEFAULTS = ["json2dir", "json2dir-zig", "json2dir-python", "json2dir-js",
            "json2dir-cs", "nuon2dir", "json2dir-scheme", "json2dir-nix"]
PACKAGES = ["git", "rustup", "zig_0_16", "python313", "nodejs_24", "dotnet-sdk_10",
            "nushell", "guile_3", "nix", "time", "util-linux", "coreutils", "gcc", "pkg-config"]


def run(*args, cwd=None, env=None):
    return subprocess.check_output(args, cwd=cwd, env=env, text=True).strip()


def source_path(work, item):
    path = (work / (item.get("source") or f"others/{item['name']}")).resolve()
    if not path.is_relative_to(work.resolve()):
        raise ValueError("source must stay inside campaign work directory")
    return path


def nix_url(locked):
    if not re.fullmatch(r"[0-9a-f]{40}", locked["rev"]):
        raise ValueError("invalid nixpkgs revision")
    return f"github:NixOS/nixpkgs/{locked['rev']}?narHash={quote(locked['narHash'], safe='')}"


def runtime_commands(item):
    # Keep adapters in the tester manifests; replace workstation-specific runtime bindings.
    item = dict(item)
    replacements = {
        "{runtimes}/python-3.13.16/bin/python3": "{runtimes}/tools/bin/python3",
        "{runtimes}/node-v24.21.0/bin/node": "{runtimes}/tools/bin/node",
        "{runtimes}/nushell-0.115.1": "{runtimes}/tools/bin",
        "{runtimes}/guile-3.0.7/guile": "{runtimes}/tools/bin/guile",
        "{runtimes}/zig-0.16.0/zig": "{runtimes}/tools/bin/zig",
        "{runtimes}/nix-portable/bin/nix": "{runtimes}/tools/bin/nix",
        "/tmp/json2dir-zig-cache": "{src}/.bench-zig-cache",
        "/tmp/zig-global-cache": "{runtimes}/zig-cache",
    }
    for field in ("command", "build"):
        if item.get(field):
            for old, new in replacements.items():
                item[field] = item[field].replace(old, new)
    # The portable-Nix warmup for root/nobody is specific to the original workstation.
    if item["name"] == "json2dir-nix":
        item["build"] = "{runtimes}/tools/bin/nix --version"
    return item


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", required=True, type=Path)
    parser.add_argument("--lock", type=Path, help="reproduce an exported lock instead of resolving current sources")
    parser.add_argument("names", nargs="*")
    args = parser.parse_args()
    campaign = args.dir.resolve()
    if (campaign / "lock.json").exists() or (campaign / "campaign.json").exists():
        parser.error("use a new campaign directory")
    prior = json.loads(args.lock.read_text()) if args.lock else None
    if prior and "packageSet" in prior.get("provisioning", {}):
        from prepare_packages import provision
        provision(campaign, args.names, prior)
        return
    work = campaign / "work"
    work.mkdir(parents=True)
    runtimes = work / "runtimes"
    runtimes.mkdir()
    if prior and prior["schemaVersion"] != 1:
        parser.error("unsupported lock version")
    names = args.names or (list(i["name"] for i in prior["implementations"]) if prior else DEFAULTS)
    if len(names) != len(set(names)):
        parser.error("duplicate implementations")
    if any(not re.fullmatch(r"[A-Za-z0-9_.-]+", name) for name in names):
        parser.error("invalid implementation name")
    if prior:
        by_name = {i["name"]: i for i in prior["implementations"]}
        items = [dict(by_name[name]) for name in names]
        nixpkgs = prior["provisioning"]["nixpkgs"]
    else:
        items = [runtime_commands(json.loads((ROOT / "implementations" / f"{name}.json").read_text())) for name in names]
        metadata = json.loads(run("nix", "flake", "metadata", "--json", "github:NixOS/nixpkgs/nixos-unstable"))
        nixpkgs = metadata["locked"]
    url = nix_url(nixpkgs)
    expression = ('let pkgs = (builtins.getFlake ' + json.dumps(url)
                  + ').legacyPackages.x86_64-linux; in pkgs.buildEnv { name = "json2dir-bench-tools"; '
                  + 'paths = [ ' + ' '.join('pkgs.' + p for p in PACKAGES) + ' ]; ignoreCollisions = true; }')
    subprocess.run(["nix", "build", "--expr", expression, "--out-link", str(runtimes / "tools")], check=True)
    env = dict(os.environ, PATH=str(runtimes / "tools/bin") + ":" + os.environ["PATH"],
               RUSTUP_HOME=str(runtimes / "rust-linux/rustup"), CARGO_HOME=str(runtimes / "rust-linux/cargo"),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    cargo_bin = Path(env["CARGO_HOME"]) / "bin"
    cargo_bin.mkdir(parents=True)
    (cargo_bin / "cargo").symlink_to(runtimes / "tools/bin/cargo")
    frozen = []
    for item in items:
        source = source_path(work, item)
        source.parent.mkdir(parents=True, exist_ok=True)
        run("git", "clone", item["repo"], str(source))
        if prior:
            run("git", "checkout", "--detach", item["revision"], cwd=source)
        item["revision"] = run("git", "rev-parse", "HEAD", cwd=source)
        frozen.append({k: item[k] for k in ("name", "repo", "revision", "command", "build", "language", "description", "source") if k in item})
    rust = prior["provisioning"].get("rust") if prior else None
    if "json2dir" in names:
        source = source_path(work, next(i for i in frozen if i["name"] == "json2dir"))
        if not rust:
            toolchain_file = source / "rust-toolchain.toml"
            if toolchain_file.exists():
                channel = tomllib.loads(toolchain_file.read_text())["toolchain"]["channel"]
            else:
                channel = (source / "rust-toolchain").read_text().strip()
            manifest_url = f"https://static.rust-lang.org/dist/channel-rust-{channel}.toml"
            with urlopen(manifest_url, timeout=60) as response:
                manifest = response.read()
            with urlopen(manifest_url + ".sha256", timeout=60) as response:
                checksum = response.read().decode().split()[0]
            if hashlib.sha256(manifest).hexdigest() != checksum:
                raise ValueError("Rust manifest checksum mismatch")
            date = tomllib.loads(manifest.decode())["date"]
            channel = f"{channel}-{date}" if channel in ("nightly", "beta") else channel
            # Freeze moving stable too, using the exact version from its manifest.
            if channel == "stable":
                channel = tomllib.loads(manifest.decode())["pkg"]["rust"]["version"].split()[0]
            rust = {"channel": channel, "manifestSha256": checksum, "manifestUrl": manifest_url}
        run("rustup", "toolchain", "install", rust["channel"], "--profile", "minimal", env=env)
        run("rustup", "override", "set", rust["channel"], cwd=source, env=env)
    versions = {}
    for tool, command in {"python": ["python3", "--version"], "node": ["node", "--version"],
                          "dotnet": ["dotnet", "--version"], "zig": ["zig", "version"],
                          "nushell": ["nu", "--version"], "guile": ["guile", "--version"],
                          "nix": ["nix", "--version"], "time": ["time", "--version"],
                          "setsid": ["setsid", "--version"], "gcc": ["gcc", "--version"]}.items():
        versions[tool] = run(*command, env=env).splitlines()[0]
    if rust:
        versions["rust"] = run("rustup", "run", rust["channel"], "rustc", "-Vv", env=env)
    # Nix verifies package/source hashes; retain the realised closure's NAR hashes too.
    closure = json.loads(run("nix", "path-info", "--recursive", "--json", str(runtimes / "tools")))
    lock = {"schemaVersion": 1, "toolchains": versions, "implementations": frozen,
            "provisioning": {"nixpkgs": nixpkgs, "rust": rust, "storeClosure": closure}}
    (campaign / "lock.json").write_text(json.dumps(lock, indent=2) + "\n")
    print(f"Prepared {campaign}; sources and toolchains are now locked. No measurements have run.")


if __name__ == "__main__":
    main()
