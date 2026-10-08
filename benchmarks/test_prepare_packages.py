import unittest
from pathlib import Path
from unittest.mock import patch

import prepare_packages as packages


class PackagedPrepareTests(unittest.TestCase):
    def test_reproduction_ignores_internal_nix_metadata(self):
        locked = {"type": "github", "owner": "monadix", "repo": "json2dirpkgs", "rev": "a" * 40, "narHash": "sha256-hash"}
        self.assertEqual(packages.package_identity(locked), packages.package_identity(dict(locked, __final=True, lastModified=123)))
        self.assertNotEqual(packages.package_identity(locked), packages.package_identity(dict(locked, rev="b" * 40)))
        self.assertNotEqual(packages.package_identity(locked), packages.package_identity(dict(locked, narHash="sha256-other")))

    def test_all_packages_and_explicit_subset(self):
        selection = {"implementations": [{"name": "json2dir-zsh"}, {"name": "json2dir-python"}]}
        self.assertEqual(packages.select_names(selection, []), ["json2dir-python", "json2dir-zsh"])
        self.assertEqual(packages.select_names(selection, ["json2dir-zsh"]), ["json2dir-zsh"])
        for names in [["json2dir"], ["json2dir-zsh", "json2dir-zsh"], ["../outside"]]:
            with self.assertRaises(ValueError):
                packages.select_names(selection, names)

    def test_package_command_replaces_workstation_build(self):
        manifest = {"name": "json2dir-python", "build": "gcc ...", "command": "{src}/main", "source": "projects/impl"}
        pin = {"owner": "owner", "repo": "impl", "rev": "a" * 40}
        output = "/nix/store/" + "b" * 32 + "-json2dir-python"
        item = packages.packaged_item(manifest, pin, output)
        self.assertEqual(item["command"], output + "/bin/json2dir-python")
        self.assertEqual(item["packagePath"], output)
        self.assertEqual(item["revision"], pin["rev"])
        self.assertIsNone(item["build"])
        self.assertNotIn("source", item)
        self.assertEqual(manifest["command"], "{src}/main")
        with self.assertRaises(ValueError):
            packages.packaged_item(manifest, pin, "/tmp/program")

    def test_download_disables_builds_and_keeps_gc_roots(self):
        paths = {"json2dir-python": "/nix/store/output"}
        with patch.object(packages, "attributes", return_value=paths), patch.object(packages.subprocess, "run") as run:
            self.assertEqual(packages.download("locked-url", "packages.x86_64-linux", list(paths), Path("/campaign/result")), paths)
        run.assert_called_once_with(["nix", "build", "--max-jobs", "0", "--out-link", "/campaign/result",
                                     "locked-url#packages.x86_64-linux.json2dir-python"], check=True)


if __name__ == "__main__":
    unittest.main()
