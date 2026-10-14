#!/usr/bin/env python3
"""Evidence accounting, not a claim of commercial readiness.

Each scenario is identified by an exact Class.Method selector, not a class-wide
substring. Manual UAT is always pending until separately attested and reviewed.
"""
import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def read_trx(directory):
    results = []
    files = sorted(Path(directory).rglob("*.trx"))
    for path in files:
        root = ET.parse(path).getroot()
        definitions = {}
        for element in root.iter():
            if local_name(element.tag) != "UnitTest":
                continue
            method = next((child for child in element if local_name(child.tag) == "TestMethod"), None)
            if method is None:
                continue
            test_id = element.get("id")
            if test_id:
                definitions[test_id] = method.get("className", "") + "." + method.get("name", "")
        for element in root.iter():
            if local_name(element.tag) != "UnitTestResult":
                continue
            name = definitions.get(element.get("testId"), element.get("testName", ""))
            results.append({"name": name, "outcome": element.get("outcome", "Unknown"),
                            "file": path.name})
    return files, results


def matches(actual, selector):
    # A single named test method, never a whole class, and no partial method name.
    normalized = actual.split("(", 1)[0].split("[", 1)[0]
    return normalized == selector or normalized.endswith("." + selector)


def validate_matrix(matrix):
    if matrix.get("schemaVersion") != 2:
        raise ValueError("Only scenario traceability schemaVersion 2 is supported.")
    requirements = matrix.get("requirements")
    if not isinstance(requirements, list) or not requirements:
        raise ValueError("Requirements must be a nonempty list.")
    ids = set()
    total_weight = 0
    for req in requirements:
        if req["id"] in ids:
            raise ValueError("Duplicate requirement ID: " + req["id"])
        ids.add(req["id"])
        scenarios = req.get("scenarios", [])
        if not scenarios:
            raise ValueError("Requirement has no acceptance scenarios: " + req["id"])
        subtotal = 0
        for scenario in scenarios:
            key = scenario["id"]
            if key in ids:
                raise ValueError("Duplicate scenario ID: " + key)
            ids.add(key)
            weight = scenario["weight"]
            if not isinstance(weight, int) or weight <= 0:
                raise ValueError("Scenario weight must be a positive integer.")
            subtotal += weight
            if scenario["kind"] == "automated":
                selectors = scenario.get("testCases", [])
                if not selectors or any("." not in selector or "*" in selector for selector in selectors):
                    raise ValueError("Automated scenario requires exact Class.Method selectors: " + key)
            elif scenario["kind"] == "manual":
                if not scenario.get("procedure"):
                    raise ValueError("Manual scenario requires an acceptance procedure: " + key)
                if scenario.get("testCases"):
                    raise ValueError("Manual scenarios cannot inherit automated test results.")
            else:
                raise ValueError("Unknown evidence kind for " + key)
        if subtotal != req["weight"]:
            raise ValueError(f"{req['id']} has scenario weight {subtotal} != {req['weight']}.")
        total_weight += req["weight"]
    if total_weight != 100:
        raise ValueError("Weights must total 100 points, not " + str(total_weight))


def assess(matrix, cases, commit, run_id=""):
    validate_matrix(matrix)
    report_requirements = []
    points_passed = 0
    max_automated = 0
    missing_tests = []
    failed_tests = []
    for req in matrix["requirements"]:
        scenarios = []
        verified = 0
        for item in req["scenarios"]:
            status = "pending_manual"
            results = []
            if item["kind"] == "automated":
                max_automated += item["weight"]
                for selector in item["testCases"]:
                    found = [case for case in cases if matches(case["name"], selector)]
                    outcomes = sorted(set(case["outcome"].casefold() for case in found))
                    selector_status = ("missing" if not found else
                                       "passed" if outcomes == ["passed"] else "failed")
                    results.append({"selector": selector, "executions": len(found),
                                    "outcomes": outcomes, "status": selector_status})
                    if selector_status == "missing":
                        missing_tests.append(selector)
                    if selector_status == "failed":
                        failed_tests.append(selector)
                status = ("passed" if all(x["status"] == "passed" for x in results)
                          else "failed" if any(x["status"] == "failed" for x in results)
                          else "missing")
                if status == "passed":
                    verified += item["weight"]
            scenarios.append({"id": item["id"], "title": item["title"],
                              "kind": item["kind"], "weight": item["weight"],
                              "status": status, "testResults": results,
                              "procedure": item.get("procedure")})
        points_passed += verified
        report_requirements.append({"id": req["id"], "name": req["name"],
                                    "weight": req["weight"], "verifiedWeight": verified,
                                    "evidencePercentOfRequirement": round(100*verified/req["weight"], 1),
                                    "scenarios": scenarios})
    return {
        "schemaVersion": 2, "commit": commit, "runId": run_id,
        "executedTestCases": len(cases),
        "verifiedScenarioCount": sum(s["status"] == "passed"
            for r in report_requirements for s in r["scenarios"]),
        "totalScenarioCount": sum(len(r["scenarios"]) for r in report_requirements),
        "verifiedAutomatedWeight": points_passed,
        "automatedWeightAvailable": max_automated,
        "totalAcceptanceWeight": 100,
        "verifiedAcceptanceEvidencePercent": points_passed,
        "automatedScenarioPassRateWeighted": round(100*points_passed/max_automated, 2)
            if max_automated else 0,
        "manualAcceptanceStatus": "not_verified",
        "commercialReadinessPercent": None,
        "releaseDecision": "NO-GO",
        "missingSelectors": sorted(set(missing_tests)),
        "failedSelectors": sorted(set(failed_tests)),
        "requirements": report_requirements,
        "explanation": "Scenario-level automated evidence only. Manual UAT and critical release gates remain unverified."
    }


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", required=True)
    parser.add_argument("--trx-dir", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--run-id", default="")
    parser.add_argument("--output", required=True)
    parser.add_argument("--strict", action="store_true",
                        help="Fail CI if a mapped automated acceptance selector is absent or nonpassed.")
    args = parser.parse_args(argv)
    matrix = json.loads(Path(args.matrix).read_text(encoding="utf-8"))
    files, cases = read_trx(args.trx_dir)
    report = assess(matrix, cases, args.commit, args.run_id)
    report["trxFileCount"] = len(files)
    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"SolidPOS verified acceptance evidence: {report['verifiedAcceptanceEvidencePercent']}/100 weighted points")
    print(f"Automated acceptance weighting: {report['verifiedAutomatedWeight']}/{report['automatedWeightAvailable']}")
    print(f"Scenarios demonstrated: {report['verifiedScenarioCount']}/{report['totalScenarioCount']}; "
          f"TRX cases: {len(cases)} across {len(files)} files; release: NO-GO")
    for req in report["requirements"]:
        print(f"  {req['id']}: {req['verifiedWeight']}/{req['weight']} verified points")
    if not files or not cases:
        print("ERROR: no usable TRX test results found.", file=sys.stderr)
        return 2
    if args.strict and (report["missingSelectors"] or report["failedSelectors"]):
        print("ERROR: mapped acceptance tests missing or failing.", file=sys.stderr)
        for selector in report["missingSelectors"]:
            print("  missing: " + selector, file=sys.stderr)
        for selector in report["failedSelectors"]:
            print("  failed: " + selector, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
