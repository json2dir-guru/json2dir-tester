#!/usr/bin/env python3
"""Select incremental work and bundle results without mixing campaign provenance."""

import argparse
import datetime
import hashlib
import json
import math
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
REUSABLE = {"ok", "timeout", "skipped", "incorrect-output", "exit-error", "left-children", "budget-exhausted"}


def read(path):
    return json.loads(path.read_text())


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def methodology():
    hashed = hashlib.sha256()
    files = [*ROOT.glob("src/**/*.cs"), *ROOT.glob("src/**/*.csproj")]
    for path in sorted(p for p in files if not {"bin", "obj"}.intersection(p.relative_to(ROOT).parts)):
        hashed.update(path.relative_to(ROOT).as_posix().encode() + b"\0")
        hashed.update(hashlib.sha256(path.read_bytes()).digest())
    return hashed.hexdigest()


def safe_path(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError("campaign path escapes the bundle")
    return path


def campaign_pair(campaign, samples):
    identity = campaign["id"]
    if (not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", identity)
            or campaign.get("schemaVersion") != 1 or samples.get("schemaVersion") != 1
            or samples.get("campaignId") != identity):
        raise ValueError("invalid or mismatched campaign files")
    keys = [(r["implementation"], r["workload"], r["storage"]) for r in campaign["results"]]
    if len(keys) != len(set(keys)):
        raise ValueError("duplicate campaign result")
    return identity, (campaign, samples)


def expected_pairs(campaign):
    storage = campaign["options"]["storage"]
    if storage not in {"both", "disk", "tmpfs"}:
        raise ValueError("invalid campaign storage")
    return {(w["id"], s) for w in campaign["workloads"]
            for s in (["disk", "tmpfs"] if storage == "both" else [storage])}


def baseline(root):
    if root is None or not root.exists():
        return {}, {}
    folder = root / "bundle" if (root / "bundle/index.json").is_file() else root
    if (folder / "index.json").is_file():
        index = read(safe_path(folder, "index.json"))
        if index.get("schemaVersion") != 1:
            raise ValueError("unsupported bundle version")
        campaigns = {}
        for identity in index["campaigns"]:
            if not re.fullmatch(r"[A-Za-z0-9_-]{1,128}", identity):
                raise ValueError("invalid campaign ID")
            pair = campaign_pair(read(safe_path(folder, f"campaigns/{identity}/benchmarks.json")),
                                 read(safe_path(folder, f"campaigns/{identity}/benchmark-samples.json")))
            if pair[0] != identity:
                raise ValueError("campaign ID differs from its directory")
            campaigns[identity] = pair[1]
        expected = {tuple(pair) for pair in index["expectedPairs"]}
        for campaign, _ in campaigns.values():
            if expected_pairs(campaign) != expected:
                raise ValueError("bundle coverage differs from its original campaigns")
            for key, value in index["options"].items():
                actual = campaign["options"].get(key)
                if key == "filter":
                    actual = actual or None
                if actual != value:
                    raise ValueError("bundle options differ from its original campaigns")
        for row in index["results"]:
            if (row["workload"], row["storage"]) not in expected:
                raise ValueError("bundle result lies outside requested coverage")
            original = {k: v for k, v in row.items() if k != "campaignId"}
            if original not in campaigns[row["campaignId"]][0]["results"]:
                raise ValueError("bundle result differs from its original campaign")
        keys = [(r["implementation"], r["workload"], r["storage"]) for r in index["results"]]
        if len(keys) != len(set(keys)):
            raise ValueError("duplicate bundle result")
        return index, campaigns
    # Older artifacts have no methodology fingerprints: retain their provenance,
    # but do not assume that their measurements match the current harness.
    for folder in (root / "export", root):
        if (folder / "benchmarks.json").is_file():
            identity, pair = campaign_pair(read(safe_path(root, folder.relative_to(root) / "benchmarks.json")),
                                           read(safe_path(root, folder.relative_to(root) / "benchmark-samples.json")))
            return {}, {identity: pair}
    raise ValueError("baseline artifact contains no benchmark bundle or export")


def plan(lock, previous, options, method_hash):
    if any(not math.isfinite(options[k]) or options[k] <= 0 for k in ("timeout", "budget")):
        raise ValueError("timeout and budget must be finite and positive")
    tools = {k: v for k, v in lock["toolchains"].items() if k != "implementations"}
    fingerprints = {}
    for item in lock["implementations"]:
        if not item.get("packagePath"):
            raise ValueError("incremental bundles require packaged implementations")
        definition = {k: item.get(k) for k in ("name", "repo", "revision", "command", "build", "packagePath")}
        fingerprints[item["name"]] = digest([definition, tools, lock.get("provisioning", {}).get("nixpkgs"), options, method_hash])
    if len(fingerprints) != len(lock["implementations"]):
        raise ValueError("duplicate locked implementation")
    compatible = {name for name, fingerprint in fingerprints.items()
                  if previous.get("fingerprints", {}).get(name) == fingerprint}
    expected = {tuple(pair) for pair in previous.get("expectedPairs", [])}
    ready = set()
    for name in compatible:
        rows = [r for r in previous["results"] if r["implementation"] == name]
        if expected and {(r["workload"], r["storage"]) for r in rows if r["status"] in REUSABLE} == expected:
            ready.add(name)
    return {"schemaVersion": 1, "options": options, "methodologySha256": method_hash,
            "fingerprints": fingerprints, "compatibleNames": sorted(compatible),
            "runNames": sorted(fingerprints.keys() - ready)}


def merge(planned, previous, campaigns, current=None):
    old = {(r["implementation"], r["workload"], r["storage"]): r for r in previous.get("results", [])
           if r["implementation"] in planned["compatibleNames"] and r["status"] in REUSABLE}
    expected = {tuple(pair) for pair in previous.get("expectedPairs", [])} if old else set()
    current_rows = {}
    if current:
        identity, pair = campaign_pair(*current)
        if identity in campaigns and campaigns[identity] != pair:
            raise ValueError("conflicting campaign ID")
        campaigns = dict(campaigns, **{identity: pair})
        c = pair[0]
        if c["environment"].get("benchmarkMethodologySha256") != planned["methodologySha256"]:
            raise ValueError("current campaign methodology differs from the incremental plan")
        actual_names = {i["definition"]["name"] for i in c["implementations"]}
        if actual_names != set(planned["runNames"]):
            raise ValueError("current campaign differs from the incremental plan")
        for key, value in planned["options"].items():
            actual = c["options"].get(key)
            if key == "filter":
                actual = actual or None
            if actual != value:
                raise ValueError("current campaign options differ from the incremental plan")
        current_fingerprints = plan(c["lock"], {}, planned["options"], planned["methodologySha256"])["fingerprints"]
        if any(current_fingerprints.get(name) != planned["fingerprints"][name] for name in actual_names):
            raise ValueError("current campaign packages or tools differ from the incremental plan")
        expected = expected_pairs(c)
        current_rows = {(r["implementation"], r["workload"], r["storage"]): dict(r, campaignId=identity)
                        for r in c["results"]}
    results = []
    for name in sorted(planned["fingerprints"]):
        for workload, storage in sorted(expected):
            key = (name, workload, storage)
            new = current_rows.get(key)
            row = new if new and new["status"] in REUSABLE else old.get(key, new)
            if row is None:
                continue
            results.append(row)
    used = {r["campaignId"] for r in results}
    if current:
        used.add(current[0]["id"])
    return {"schemaVersion": 1, "created": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "options": planned["options"], "methodologySha256": planned["methodologySha256"],
            "fingerprints": planned["fingerprints"], "expectedPairs": [list(pair) for pair in sorted(expected)],
            "campaigns": sorted(used), "results": results,
            "unmeasuredNames": sorted(set(planned["fingerprints"]) - {r["implementation"] for r in results})}, campaigns


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["plan", "merge"])
    parser.add_argument("--campaign", required=True, type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--plan", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--suite", choices=["standard", "extended"], default="standard")
    parser.add_argument("--filter", default="")
    parser.add_argument("--timeout", type=float, default=30)
    parser.add_argument("--budget", type=float, default=240)
    args = parser.parse_args()
    prior, campaigns = baseline(args.baseline)
    if args.mode == "plan":
        options = {"suite": args.suite, "filter": args.filter or None, "storage": "both",
                   "timeout": args.timeout, "budget": args.budget, "seed": 1729,
                   "warmups": 3, "repetitions": 15, "profiles": 3}
        planned = plan(read(args.campaign / "lock.json"), prior, options, methodology())
        write(args.plan, planned)
        print(f"Run {len(planned['runNames'])} implementations; reuse {len(planned['fingerprints']) - len(planned['runNames'])} complete entries.")
    else:
        if args.out is None:
            parser.error("merge requires --out")
        export = args.campaign.parent / "export"
        current = (read(export / "benchmarks.json"), read(export / "benchmark-samples.json")) if (export / "benchmarks.json").exists() else None
        index, campaigns = merge(read(args.plan), prior, campaigns, current)
        if args.out.exists():
            raise ValueError("use a new bundle output directory")
        for identity in index["campaigns"]:
            campaign, samples = campaigns[identity]
            write(args.out / "campaigns" / identity / "benchmarks.json", campaign)
            write(args.out / "campaigns" / identity / "benchmark-samples.json", samples)
            write(args.out / "campaigns" / identity / "lock.json", campaign["lock"])
        write(args.out / "index.json", index)
        print(f"Bundled {len(index['results'])} results from {len(index['campaigns'])} campaigns.")


if __name__ == "__main__":
    main()
