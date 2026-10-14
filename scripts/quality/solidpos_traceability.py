#!/usr/bin/env python3
"""Generate honest SolidPOS requirement-level evidence from Windows dotnet TRX files."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET

def read_trx(directory):
    cases = []
    for path in sorted(Path(directory).rglob("*.trx")):
        root = ET.parse(path).getroot()
        definitions = {}
        for element in root.iter():
            if element.tag.rsplit("}", 1)[-1] == "UnitTest":
                test_id = element.attrib.get("id")
                method = next((n for n in element if n.tag.rsplit("}", 1)[-1] == "TestMethod"), None)
                if test_id:
                    definitions[test_id] = (method.attrib.get("className", "") + "." + method.attrib.get("name", "")) if method is not None else element.attrib.get("name", "")
        for element in root.iter():
            if element.tag.rsplit("}", 1)[-1] == "UnitTestResult":
                cases.append({"name": definitions.get(element.attrib.get("testId"), element.attrib.get("testName", "")),
                              "outcome": element.attrib.get("outcome", "Unknown"), "file": str(path)})
    return cases

def assess(matrix, cases, commit):
    requirements = []
    total_weight = sum(item["weight"] for item in matrix["requirements"])
    verified_weight = 0
    for item in matrix["requirements"]:
        groups = []
        for token in item["tests"]:
            matched = [case for case in cases if token.casefold() in case["name"].casefold()]
            passed = sum(case["outcome"].casefold() == "passed" for case in matched)
            failed = sum(case["outcome"].casefold() not in ("passed",) for case in matched)
            groups.append({"testGroup": token, "discovered": len(matched), "passed": passed, "nonPassed": failed})
        automated = all(group["discovered"] > 0 and group["nonPassed"] == 0 for group in groups)
        if automated:
            verified_weight += item["weight"]
        requirements.append({"id": item["id"], "name": item["name"], "weight": item["weight"],
                             "automatedStatus": "passed" if automated else "not_demonstrated",
                             "testGroups": groups, "manualGate": item["manualGate"],
                             "commerciallyAccepted": False})
    return {"schemaVersion": 1, "commit": commit, "discoveredTestExecutions": len(cases),
            "automatedEvidenceWeight": verified_weight, "totalWeight": total_weight,
            "automatedEvidencePercent": round(100 * verified_weight / total_weight, 2) if total_weight else 0,
            "commercialAcceptancePercent": None,
            "releaseDecision": "NO-GO",
            "explanation": "Automated evidence is not commercial acceptance. Manual gates have not been verified.",
            "requirements": requirements}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", required=True)
    parser.add_argument("--trx-dir", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    matrix = json.loads(Path(args.matrix).read_text(encoding="utf-8"))
    report = assess(matrix, read_trx(args.trx_dir), args.commit)
    path = Path(args.output)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"SolidPOS automated evidence: {report['automatedEvidencePercent']}% "
          f"({report['automatedEvidenceWeight']}/{report['totalWeight']} weighted points); "
          f"commercial decision: {report['releaseDecision']}")
    for item in report["requirements"]:
        print(f"  {item['id']}: {item['automatedStatus']} ({item['weight']} points)")
    # Missing automated tests are reported, not silently counted as passing.
    # Overall CI build/test failures remain enforced by the dotnet test step.

if __name__ == "__main__":
    main()
