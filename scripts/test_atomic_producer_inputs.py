"""Mutation controls for the current-versus-reviewed acceptance guard."""
import unittest
from verify_atomic_producer_inputs import require_dependency_pins, require_equal, select_inputs


class ProducerInputGuardTests(unittest.TestCase):
    def setUp(self):
        self.reviewed = {"Legacy.Maliev.AccountingService.Api/Program.cs": "100644 blob producer",
                         "Directory.Build.props": "100644 blob props", "nuget.config": "100644 blob nuget"}

    def test_matching_production_and_inputs_pass(self):
        require_equal(dict(self.reviewed), self.reviewed)

    def test_root_props_drift_fails(self):
        changed = dict(self.reviewed, **{"Directory.Build.props": "100644 blob changed"})
        with self.assertRaises(ValueError):
            require_equal(changed, self.reviewed)

    def test_added_root_input_fails(self):
        changed = dict(self.reviewed, **{"global.json": "100644 blob added"})
        with self.assertRaises(ValueError):
            require_equal(changed, self.reviewed)

    def test_removed_root_input_fails(self):
        changed = dict(self.reviewed)
        del changed["nuget.config"]
        with self.assertRaises(ValueError):
            require_equal(changed, self.reviewed)

    def test_selection_includes_root_input_case_and_patterns(self):
        entries = dict(self.reviewed, **{"NuGet.Config": "config", "Directory.Packages.targets": "packages",
                                       "Directory.Build.targets": "targets", "global.json": "sdk",
                                       "Accounting.slnx": "solution", "README.md": "documentation"})
        selected = select_inputs(entries)
        self.assertEqual(set(entries) - {"README.md"}, set(selected))

    def test_dependency_pin_drift_missing_and_duplicate_fail(self):
        expected = {"Legacy.Maliev.ServiceDefaults": "defaults", "Legacy.Maliev.CompatibilityContracts": "contracts"}
        source = "  repository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults\n  ref: defaults\n  repository: MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts\n  ref: contracts\n"
        require_dependency_pins(source, expected)
        for changed in (source.replace("ref: defaults", "ref: drift"), source.split("  repository: MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts")[0], source + source):
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                require_dependency_pins(changed, expected)

    def test_empty_producer_fails(self):
        with self.assertRaises(ValueError):
            require_equal({}, {})


if __name__ == "__main__":
    unittest.main()
