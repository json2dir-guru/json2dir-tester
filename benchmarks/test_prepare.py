import unittest
from pathlib import Path

import prepare


class PrepareTests(unittest.TestCase):
    def test_source_overrides_stay_in_workspace(self):
        work = Path("/tmp/json2dir-campaign/work")
        self.assertEqual(prepare.source_path(work, {"name": "impl"}), work / "others/impl")
        self.assertEqual(prepare.source_path(work, {"name": "impl", "source": None}), work / "others/impl")
        self.assertEqual(prepare.source_path(work, {"name": "impl", "source": "projects/impl"}), work / "projects/impl")
        with self.assertRaises(ValueError):
            prepare.source_path(work, {"name": "impl", "source": "../outside"})

    def test_adapters_keep_manifest_command(self):
        manifest = {"name": "json2dir-python", "command": "{runtimes}/python-3.13.16/bin/python3 -I {src}/json2dir.py"}
        rewritten = prepare.runtime_commands(manifest)
        self.assertEqual(rewritten["command"], "{runtimes}/tools/bin/python3 -I {src}/json2dir.py")
        self.assertIn("python-3.13.16", manifest["command"])

    def test_immutable_nix_url(self):
        locked = {"rev": "a" * 40, "narHash": "sha256-abc+/="}
        self.assertEqual(prepare.nix_url(locked), "github:NixOS/nixpkgs/" + "a" * 40 + "?narHash=sha256-abc%2B%2F%3D")
        with self.assertRaises(ValueError):
            prepare.nix_url({"rev": "nixos-unstable", "narHash": "hash"})


if __name__ == "__main__":
    unittest.main()
