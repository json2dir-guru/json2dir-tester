import copy
import json
from pathlib import Path
import tempfile
import unittest

import bundle


class BundleTests(unittest.TestCase):
    def setUp(self):
        self.options = {"suite": "standard", "filter": None, "storage": "both", "timeout": 30,
                        "budget": 240, "seed": 1729, "warmups": 3, "repetitions": 15, "profiles": 3}
        self.lock = {"toolchains": {"dotnet": "10", "implementations": "package-set old"},
                     "implementations": [self.definition("a"), self.definition("b")],
                     "provisioning": {"nixpkgs": {"rev": "pinned", "narHash": "hash"}}}

    def definition(self, name):
        return {"name": name, "repo": "repo", "revision": "rev", "command": f"/nix/store/{name}/bin/{name}",
                "build": None, "packagePath": f"/nix/store/{name}"}

    def campaign(self, identity, names, incomplete=False):
        rows = [{"implementation": name, "workload": w, "storage": s,
                 "status": "incomplete" if incomplete and w == "files" else "ok", "reason": "",
                 "timing": {"count": 15, "median": 1} if not incomplete or w != "files" else None}
                for name in names for w in ["empty", "files"] for s in ["disk", "tmpfs"]]
        campaign = {"schemaVersion": 1, "id": identity, "created": identity, "options": self.options,
                    "lock": self.lock, "environment": {"cpu": identity, "benchmarkMethodologySha256": "method"}, "completed": not incomplete,
                    "implementations": [{"definition": self.definition(n)} for n in names],
                    "workloads": [{"id": "empty"}, {"id": "files"}], "results": rows}
        return campaign, {"schemaVersion": 1, "campaignId": identity, "samples": []}

    def initial(self, incomplete=False):
        planned = bundle.plan(self.lock, {}, self.options, "method")
        return bundle.merge(planned, {}, {}, self.campaign("first", ["a", "b"], incomplete))

    def test_only_added_changed_and_unfinished_run(self):
        index, _ = self.initial()
        self.assertEqual(bundle.plan(self.lock, index, self.options, "method")["runNames"], [])
        changed = copy.deepcopy(self.lock)
        changed["implementations"][1]["packagePath"] += "-new"
        changed["implementations"].append(self.definition("c"))
        self.assertEqual(bundle.plan(changed, index, self.options, "method")["runNames"], ["b", "c"])
        partial, _ = self.initial(True)
        self.assertEqual(bundle.plan(self.lock, partial, self.options, "method")["runNames"], ["a", "b"])

    def test_method_tools_or_options_invalidate_reuse_but_package_set_label_does_not(self):
        index, _ = self.initial()
        self.assertEqual(bundle.plan(self.lock, index, self.options, "new-method")["runNames"], ["a", "b"])
        tools = copy.deepcopy(self.lock)
        tools["toolchains"]["dotnet"] = "11"
        self.assertEqual(bundle.plan(tools, index, self.options, "method")["runNames"], ["a", "b"])
        self.assertEqual(bundle.plan(self.lock, index, dict(self.options, repetitions=5), "method")["runNames"], ["a", "b"])
        tools = copy.deepcopy(self.lock)
        tools["provisioning"]["nixpkgs"]["rev"] = "new"
        self.assertEqual(bundle.plan(tools, index, self.options, "method")["runNames"], ["a", "b"])
        tools = copy.deepcopy(self.lock)
        tools["toolchains"]["implementations"] = "package-set new"
        self.assertEqual(bundle.plan(tools, index, self.options, "method")["runNames"], [])

    def test_complete_bundle_retains_original_campaigns_samples_and_environments(self):
        index, campaigns = self.initial()
        changed = copy.deepcopy(self.lock)
        changed["implementations"].append(self.definition("c"))
        planned = bundle.plan(changed, index, self.options, "method")
        self.lock = changed
        current = self.campaign("second", ["c"])
        merged, archives = bundle.merge(planned, index, campaigns, current)
        self.assertEqual(len(merged["results"]), 12)
        self.assertEqual(merged["campaigns"], ["first", "second"])
        self.assertEqual(archives["first"], campaigns["first"])
        self.assertEqual(archives["second"], current)
        self.assertEqual({r["campaignId"] for r in merged["results"] if r["implementation"] == "a"}, {"first"})

    def test_partial_new_campaign_preserves_compatible_completed_old_pairs(self):
        original = self.campaign("first", ["a", "b"], True)
        # One implementation completed while the other was interrupted.
        for row in original[0]["results"]:
            if row["implementation"] == "a" and row["workload"] == "files":
                row["status"] = "ok"
        index, campaigns = bundle.merge(bundle.plan(self.lock, {}, self.options, "method"), {}, {}, original)
        planned = bundle.plan(self.lock, index, self.options, "method")
        self.assertEqual(planned["runNames"], ["b"])
        current = self.campaign("second", ["b"], True)
        merged, _ = bundle.merge(planned, index, campaigns, current)
        self.assertEqual({r["campaignId"] for r in merged["results"] if r["implementation"] == "a"}, {"first"})
        self.assertTrue(all(r["status"] == "incomplete" for r in merged["results"] if r["implementation"] == "b" and r["workload"] == "files"))

    def test_no_new_measurements_still_produces_complete_download(self):
        index, campaigns = self.initial()
        merged, archives = bundle.merge(bundle.plan(self.lock, index, self.options, "method"), index, campaigns)
        self.assertEqual(merged["results"], index["results"])
        self.assertEqual(merged["unmeasuredNames"], [])
        self.assertEqual(archives, campaigns)

    def test_wrong_options_packages_and_campaign_ids_are_rejected(self):
        planned = bundle.plan(self.lock, {}, self.options, "method")
        current = self.campaign("first", ["a", "b"])
        current[1]["campaignId"] = "other"
        with self.assertRaises(ValueError):
            bundle.merge(planned, {}, {}, current)
        current = self.campaign("first", ["a", "b"])
        current[0]["options"] = dict(self.options, timeout=10)
        with self.assertRaises(ValueError):
            bundle.merge(planned, {}, {}, current)
        current = self.campaign("first", ["a", "b"])
        current[0]["lock"] = copy.deepcopy(self.lock)
        current[0]["lock"]["implementations"][0]["packagePath"] += "-wrong"
        with self.assertRaises(ValueError):
            bundle.merge(planned, {}, {}, current)

    def test_resource_failures_retry_and_removed_packages_leave_current_index(self):
        current = self.campaign("first", ["a", "b"])
        current[0]["results"][0]["status"] = "resource-error"
        index, campaigns = bundle.merge(bundle.plan(self.lock, {}, self.options, "method"), {}, {}, current)
        self.assertEqual(bundle.plan(self.lock, index, self.options, "method")["runNames"], ["a"])
        reduced = copy.deepcopy(self.lock)
        reduced["implementations"] = [self.definition("b")]
        merged, _ = bundle.merge(bundle.plan(reduced, index, self.options, "method"), index, campaigns)
        self.assertEqual({r["implementation"] for r in merged["results"]}, {"b"})
        self.assertEqual(merged["unmeasuredNames"], [])

    def test_wrong_methodology_is_rejected(self):
        planned = bundle.plan(self.lock, {}, self.options, "method")
        current = self.campaign("first", ["a", "b"])
        current[0]["environment"]["benchmarkMethodologySha256"] = "other"
        with self.assertRaises(ValueError):
            bundle.merge(planned, {}, {}, current)

    def test_bundle_path_traversal_and_changed_summary_are_rejected(self):
        index, campaigns = self.initial()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            bundle.write(root / "index.json", index)
            for identity, pair in campaigns.items():
                bundle.write(root / "campaigns" / identity / "benchmarks.json", pair[0])
                bundle.write(root / "campaigns" / identity / "benchmark-samples.json", pair[1])
            self.assertEqual(bundle.baseline(root), (index, campaigns))
            bad = copy.deepcopy(index)
            bad["expectedPairs"] = [["empty", "disk"]]
            bad["results"] = [r for r in bad["results"] if r["workload"] == "empty" and r["storage"] == "disk"]
            bundle.write(root / "index.json", bad)
            with self.assertRaises(ValueError):
                bundle.baseline(root)
            bad = copy.deepcopy(index)
            bad["results"][0]["timing"]["median"] = 999
            bundle.write(root / "index.json", bad)
            with self.assertRaises(ValueError):
                bundle.baseline(root)
            bad = dict(index, campaigns=["../outside"])
            bundle.write(root / "index.json", bad)
            with self.assertRaises(ValueError):
                bundle.baseline(root)
            with self.assertRaises(ValueError):
                bundle.safe_path(root, "../outside")

    def test_legacy_export_is_not_silently_reused(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            pair = self.campaign("legacy", ["a", "b"])
            bundle.write(root / "export/benchmarks.json", pair[0])
            bundle.write(root / "export/benchmark-samples.json", pair[1])
            index, campaigns = bundle.baseline(root)
            self.assertEqual(index, {})
            self.assertIn("legacy", campaigns)
            self.assertEqual(bundle.plan(self.lock, index, self.options, "method")["runNames"], ["a", "b"])


if __name__ == "__main__":
    unittest.main()
