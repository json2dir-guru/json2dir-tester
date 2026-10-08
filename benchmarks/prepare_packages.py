#!/usr/bin/env python3
"""Prepare cached json2dirpkgs executables for a benchmark campaign; never build."""

import argparse
import json
import os
from pathlib import Path
import re
import shlex
import subprocess

from prepare import nix_url, run

DEFAULT_REF = "github:monadix/json2dirpkgs/2dd32280eba87e4781a16252440fe71fb0167280"
TOOLS = ["coreutils", "dotnet-sdk_10", "git", "nix", "python3", "time", "util-linux"]


def package_identity(locked):
    # Ignore internal metadata (such as __final) that varies between Nix versions.
    return {key: locked[key] for key in ("type", "owner", "repo", "rev", "narHash")}


def select_names(selection, requested):
    available = {item["name"] for item in selection["implementations"]}
    names = requested or sorted(available)
    if len(names) != len(set(names)):
        raise ValueError("duplicate implementations")
    for name in names:
        if not re.fullmatch(r"[A-Za-z0-9_.-]+", name) or name not in available:
            raise ValueError(f"implementation is not packaged: {name}")
    if not names:
        raise ValueError("no packaged implementations selected")
    return names


def packaged_item(manifest, pin, output):
    name = manifest["name"]
    if not re.fullmatch(r"[A-Za-z0-9_.-]+", name) or not re.fullmatch(r"/nix/store/[a-z0-9]{32}-[^/]+", output):
        raise ValueError("invalid package name or store output")
    return {"name": name, "repo": f"https://github.com/{pin['owner']}/{pin['repo']}",
            "revision": pin["rev"], "command": shlex.quote(f"{output}/bin/{name}"),
            "build": None, "packagePath": output,
            "language": manifest.get("language", ""), "description": manifest.get("description", "")}


def attributes(url, namespace, names):
    expression = ('p: builtins.listToAttrs (map (n: { name = n; value = p.${n}.outPath; }) [ '
                  + ' '.join(json.dumps(n) for n in names) + ' ])')
    return json.loads(run("nix", "eval", "--json", f"{url}#{namespace}", "--apply", expression))


def download(url, namespace, names, link):
    paths = attributes(url, namespace, names)
    # Keep GC roots for the duration of the campaign; a cache miss is a setup error.
    subprocess.run(["nix", "build", "--max-jobs", "0", "--out-link", str(link),
                    *[f"{url}#{namespace}.{name}" for name in names]], check=True)
    return paths


def provision(campaign, names=None, prior=None, package_ref=DEFAULT_REF):
    campaign = campaign.resolve()
    if (campaign / "lock.json").exists() or (campaign / "campaign.json").exists():
        raise ValueError("use a new campaign directory")
    if prior and (prior.get("schemaVersion") != 1 or "packageSet" not in prior.get("provisioning", {})):
        raise ValueError("expected a packaged campaign lock")
    ref = prior["provisioning"]["packageSet"]["url"] if prior else package_ref
    metadata = json.loads(run("nix", "flake", "metadata", "--json", "--no-write-lock-file", ref))
    if prior and package_identity(metadata["locked"]) != package_identity(prior["provisioning"]["packageSet"]["locked"]):
        raise ValueError("package set differs from the campaign lock")
    # Metadata may name a source store path without materializing its contents.
    archive = json.loads(run("nix", "flake", "archive", "--json", "--no-write-lock-file", metadata["url"]))
    source = Path(archive["path"])
    selection = json.loads((source / "data/selection.json").read_text())
    names = select_names(selection, names or ([i["name"] for i in prior["implementations"]] if prior else []))
    nixpkgs = metadata["locks"]["nodes"]["nixpkgs"]["locked"]
    runtimes = campaign / "work/runtimes"
    tools_bin = runtimes / "tools/bin"
    tools_bin.mkdir(parents=True)
    (runtimes / "packages").mkdir()
    packages = download(metadata["url"], "packages.x86_64-linux", names, runtimes / "packages/result")
    tools = download(nix_url(nixpkgs), "legacyPackages.x86_64-linux", TOOLS, runtimes / "tools/result")
    for path in tools.values():
        for executable in (Path(path) / "bin").glob("*"):
            target = tools_bin / executable.name
            if not target.is_symlink():
                target.symlink_to(executable)
    manifests = {item["name"]: item["manifest"] for item in selection["implementations"]}
    pins = {name: json.loads((source / "sources" / f"{name}.json").read_text()) for name in names}
    frozen = [packaged_item(manifests[name], pins[name], packages[name]) for name in names]
    if prior:
        expected = {i["name"]: i for i in prior["implementations"]}
        if any(item != expected[item["name"]] for item in frozen):
            raise ValueError("packaged commands differ from the campaign lock")
    env = dict(os.environ, PATH=str(tools_bin) + ":" + os.environ["PATH"], DOTNET_CLI_TELEMETRY_OPTOUT="1")
    versions = {name: run(*command, env=env).splitlines()[0] for name, command in {
        "dotnet": ["dotnet", "--version"], "python": ["python3", "--version"],
        "nix": ["nix", "--version"], "time": ["time", "--version"], "setsid": ["setsid", "--version"]}.items()}
    versions["implementations"] = "json2dirpkgs " + metadata["locked"]["rev"] + "; packaged executables"
    closure = json.loads(run("nix", "path-info", "--recursive", "--json", *packages.values(), *tools.values()))
    lock = {"schemaVersion": 1, "toolchains": versions, "implementations": frozen,
            "provisioning": {"nixpkgs": nixpkgs, "packageSet": {"url": metadata["url"], "locked": metadata["locked"]},
                             "sourcePins": pins, "storeClosure": closure}}
    (campaign / "lock.json").write_text(json.dumps(lock, indent=2) + "\n")
    print(f"Prepared {len(frozen)} cached implementations in {campaign}; no builds or measurements ran.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dir", required=True, type=Path)
    parser.add_argument("--lock", type=Path)
    parser.add_argument("--packages-ref", default=DEFAULT_REF, help="GitHub json2dirpkgs flake reference; resolved revision is locked")
    parser.add_argument("names", nargs="*")
    args = parser.parse_args()
    provision(args.dir, args.names, json.loads(args.lock.read_text()) if args.lock else None, args.packages_ref)


if __name__ == "__main__":
    main()
