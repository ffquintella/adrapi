#!/usr/bin/env python3
import argparse
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def run(cmd):
    return subprocess.check_output(cmd, text=True).strip()


def resolve_default_base(head_sha):
    env_base = os.environ.get("GITHUB_BASE_SHA")
    if env_base:
        return env_base

    env_base_ref = os.environ.get("GITHUB_BASE_REF")
    if env_base_ref:
        try:
            return run(["git", "merge-base", f"origin/{env_base_ref}", head_sha])
        except Exception:
            pass

    return None


def changed_source_files(base_sha, head_sha):
    try:
        diff = run(["git", "diff", "--name-only", f"{base_sha}...{head_sha}"])
    except Exception:
        diff = run(["git", "diff", "--name-only", f"{base_sha}", f"{head_sha}"])
    files = []
    for line in diff.splitlines():
        path = line.strip().replace("\\", "/")
        if not path.endswith(".cs"):
            continue
        if path.startswith("tests/"):
            continue
        if path.startswith("adrapi/") or path.startswith("domain/"):
            files.append(path)
    return sorted(set(files))


def find_latest_coverage_file(root):
    matches = sorted(Path(root).glob("**/coverage.cobertura.xml"), key=lambda p: p.stat().st_mtime, reverse=True)
    return matches[0] if matches else None


def parse_coverage(path):
    tree = ET.parse(path)
    root = tree.getroot()
    coverage = {}

    for cls in root.findall(".//class"):
        filename = (cls.attrib.get("filename") or "").replace("\\", "/")
        if not filename:
            continue

        valid = 0
        covered = 0
        lines = cls.find("lines")
        if lines is not None:
            for line in lines.findall("line"):
                valid += 1
                if int(line.attrib.get("hits", "0")) > 0:
                    covered += 1

        if filename not in coverage:
            coverage[filename] = [0, 0]
        coverage[filename][0] += covered
        coverage[filename][1] += valid

    return coverage


def main():
    parser = argparse.ArgumentParser(description="Fail build when changed source coverage is below threshold.")
    parser.add_argument("--coverage-root", required=True, help="Coverage root folder (contains coverage.cobertura.xml).")
    parser.add_argument("--threshold", type=float, default=0.70, help="Minimum changed-source line coverage [0-1].")
    parser.add_argument("--base-sha", default=None, help="Base commit sha for diff.")
    parser.add_argument("--head-sha", default=None, help="Head commit sha for diff.")
    args = parser.parse_args()

    head_sha = args.head_sha or os.environ.get("GITHUB_SHA") or run(["git", "rev-parse", "HEAD"])
    base_sha = args.base_sha or resolve_default_base(head_sha)

    if not base_sha:
        print("Coverage gate skipped: no base commit provided (enforced in CI via GITHUB_BASE_SHA).")
        return 0

    changed = changed_source_files(base_sha, head_sha)
    if not changed:
        print("Coverage gate skipped: no changed source files under adrapi/ or domain/.")
        return 0

    cov_file = find_latest_coverage_file(args.coverage_root)
    if not cov_file:
        print(f"Coverage gate failed: no coverage.cobertura.xml found under {args.coverage_root}.")
        return 1

    coverage = parse_coverage(cov_file)

    missing = []
    total_covered = 0
    total_valid = 0
    per_file = []

    for file in changed:
        entry = coverage.get(file)
        if not entry:
            missing.append(file)
            continue
        covered, valid = entry
        total_covered += covered
        total_valid += valid
        rate = (covered / valid) if valid > 0 else 1.0
        per_file.append((file, covered, valid, rate))

    print(f"Coverage source: {cov_file}")
    print(f"Diff range: {base_sha}...{head_sha}")
    print("Changed source files:")
    for file in changed:
        print(f"  - {file}")

    if missing:
        print("Coverage gate failed: changed files missing from coverage report:")
        for file in missing:
            print(f"  - {file}")
        return 1

    if total_valid == 0:
        print("Coverage gate failed: changed files have zero instrumented lines.")
        return 1

    overall = total_covered / total_valid
    print("Per-file changed coverage:")
    for file, covered, valid, rate in per_file:
        print(f"  - {file}: {covered}/{valid} ({rate:.2%})")

    print(f"Changed-module coverage: {total_covered}/{total_valid} ({overall:.2%})")
    print(f"Required threshold: {args.threshold:.2%}")

    if overall < args.threshold:
        print("Coverage gate failed: changed-module coverage is below threshold.")
        return 1

    print("Coverage gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
