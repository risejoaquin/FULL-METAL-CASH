"""Regression tests for the release evidence calculator."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts" / "quality"))
import solidpos_traceability as trace


class TraceabilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.matrix = json.loads((ROOT / "docs" / "engineering" /
            "solidpos-mvp-acceptance-matrix.json").read_text(encoding="utf-8"))

    def test_manifest_has_100_points_and_unique_ids(self):
        trace.validate_matrix(self.matrix)
        self.assertEqual(sum(r["weight"] for r in self.matrix["requirements"]), 100)
        scenarios = [s for r in self.matrix["requirements"] for s in r["scenarios"]]
        self.assertEqual(len(scenarios), len({s["id"] for s in scenarios}))
        self.assertTrue(any(s["kind"] == "manual" for s in scenarios))

    def test_exact_methods_never_credit_neighbors(self):
        selector = "LocalAuthServiceTests.LoginAsync_BlocksWhenOfflineWindowExpired"
        self.assertTrue(trace.matches("SolidPOS.PosCore.UnitTests." + selector, selector))
        self.assertFalse(trace.matches("SolidPOS.PosCore.UnitTests." + selector + "Extra", selector))
        self.assertFalse(trace.matches("SolidPOS.PosCore.UnitTests.LocalAuthServiceTests.SomeOtherMethod", selector))

    def test_fully_green_automation_does_not_grant_commercial_go(self):
        cases = [{"name": "SolidPOS.PosCore.UnitTests." + method, "outcome": "Passed"}
                 for req in self.matrix["requirements"] for scenario in req["scenarios"]
                 if scenario["kind"] == "automated" for method in scenario["testCases"]]
        report = trace.assess(self.matrix, cases, "fake-commit")
        self.assertEqual(report["verifiedAcceptanceEvidencePercent"],
                         report["automatedWeightAvailable"])
        self.assertLess(report["verifiedAcceptanceEvidencePercent"], 100)
        self.assertIsNone(report["commercialReadinessPercent"])
        self.assertEqual(report["releaseDecision"], "NO-GO")
        self.assertTrue(any(s["status"] == "pending_manual"
            for r in report["requirements"] for s in r["scenarios"]))

    def test_missing_and_failed_methods_remove_only_their_scenario_points(self):
        cases = [{"name": "SolidPOS.PosCore.UnitTests." + method, "outcome": "Passed"}
                 for req in self.matrix["requirements"] for scenario in req["scenarios"]
                 if scenario["kind"] == "automated" for method in scenario["testCases"]]
        original = trace.assess(self.matrix, cases, "fake")
        cases = [c for c in cases if not c["name"].endswith(
            "LocalAuthServiceTests.LoginAsync_BlocksWhenOfflineWindowExpired")]
        missing = trace.assess(self.matrix, cases, "fake")
        self.assertIn("LocalAuthServiceTests.LoginAsync_BlocksWhenOfflineWindowExpired",
                      missing["missingSelectors"])
        self.assertLess(missing["verifiedAcceptanceEvidencePercent"],
                        original["verifiedAcceptanceEvidencePercent"])
        cases.append({"name": "SolidPOS.PosCore.UnitTests.LocalAuthServiceTests.LoginAsync_BlocksWhenOfflineWindowExpired",
                      "outcome": "Failed"})
        failed = trace.assess(self.matrix, cases, "fake")
        self.assertIn("LocalAuthServiceTests.LoginAsync_BlocksWhenOfflineWindowExpired",
                      failed["failedSelectors"])

    def test_trx_reads_namespace_and_results(self):
        xml = """<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <TestDefinitions><UnitTest id="1" name="some test">
            <TestMethod className="Example.Namespace.WidgetTests" name="Works"/>
          </UnitTest></TestDefinitions>
          <Results><UnitTestResult testId="1" outcome="Passed" testName="Works"/></Results>
        </TestRun>"""
        with tempfile.TemporaryDirectory() as td:
            Path(td, "results.trx").write_text(xml, encoding="utf-8")
            files, cases = trace.read_trx(td)
            self.assertEqual(len(files), 1)
            self.assertEqual(cases[0]["name"], "Example.Namespace.WidgetTests.Works")
            self.assertEqual(cases[0]["outcome"], "Passed")


if __name__ == "__main__":
    unittest.main()
