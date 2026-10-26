import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CHECKER = REPOSITORY_ROOT / "scripts" / "check_changed_coverage.py"
EMPTY_TREE_SHA = "4b825dc642cb6eb9a060e54bf8d69288fbee4904"
ZERO_SHA = "0" * 40


class ChangedCoverageScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self.temp_directory.name)
        self.git("init", "--quiet")
        self.git("config", "user.email", "tests@example.invalid")
        self.git("config", "user.name", "Coverage script tests")

    def tearDown(self):
        self.temp_directory.cleanup()

    def git(self, *args):
        return subprocess.run(
            ["git", *args],
            cwd=self.repository,
            check=True,
            text=True,
            capture_output=True,
        ).stdout.strip()

    def commit(self, message):
        self.git("add", ".")
        self.git("commit", "--quiet", "-m", message)
        return self.git("rev-parse", "HEAD")

    def add_source_file(self, name):
        source_file = self.repository / "adrapi" / name
        source_file.parent.mkdir(parents=True, exist_ok=True)
        source_file.write_text("public class Example { }\n", encoding="utf-8")

    def write_coverage(self, *files, hits=1):
        coverage_directory = self.repository / "coverage"
        coverage_directory.mkdir()
        classes = "".join(
            f'<class filename="adrapi/{name}"><lines><line number="1" hits="{hits}" /></lines></class>'
            for name in files
        )
        (coverage_directory / "coverage.cobertura.xml").write_text(
            f"<coverage><packages><package><classes>{classes}</classes></package></packages></coverage>",
            encoding="utf-8",
        )
        return coverage_directory

    def run_checker(self, coverage_directory, *args, environment=None):
        env = {
            key: value
            for key, value in os.environ.items()
            if key not in {"GITHUB_BASE_REF", "GITHUB_BASE_SHA", "GITHUB_SHA"}
        }
        if environment:
            env.update(environment)
        return subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                "--coverage-root",
                str(coverage_directory),
                *args,
            ],
            cwd=self.repository,
            text=True,
            capture_output=True,
            env=env,
        )

    def test_zero_base_sha_uses_head_parent_for_new_branch_pushes(self):
        self.add_source_file("Existing.cs")
        self.commit("initial")
        self.add_source_file("Changed.cs")
        head_sha = self.commit("changed source")
        parent_sha = self.git("rev-parse", "HEAD^")

        result = self.run_checker(
            self.write_coverage("Changed.cs"),
            "--base-sha",
            ZERO_SHA,
            "--head-sha",
            head_sha,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"Diff range: {parent_sha}...{head_sha}", result.stdout)
        self.assertIn("Coverage gate passed.", result.stdout)

    def test_zero_base_sha_uses_empty_tree_for_initial_commit(self):
        self.add_source_file("Initial.cs")
        head_sha = self.commit("initial")

        result = self.run_checker(
            self.write_coverage("Initial.cs"),
            "--base-sha",
            ZERO_SHA,
            "--head-sha",
            head_sha,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("", result.stderr)
        self.assertIn(f"Diff range: {EMPTY_TREE_SHA}...{head_sha}", result.stdout)
        self.assertIn("Coverage gate passed.", result.stdout)

    def test_zero_base_sha_from_environment_uses_head_parent(self):
        self.add_source_file("Existing.cs")
        self.commit("initial")
        self.add_source_file("Changed.cs")
        head_sha = self.commit("changed source")
        parent_sha = self.git("rev-parse", "HEAD^")

        result = self.run_checker(
            self.write_coverage("Changed.cs"),
            environment={"GITHUB_BASE_SHA": ZERO_SHA, "GITHUB_SHA": head_sha},
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"Diff range: {parent_sha}...{head_sha}", result.stdout)

    def test_zero_base_sha_still_enforces_the_coverage_threshold(self):
        self.add_source_file("Existing.cs")
        self.commit("initial")
        self.add_source_file("Changed.cs")
        head_sha = self.commit("changed source")

        result = self.run_checker(
            self.write_coverage("Changed.cs", hits=0),
            "--base-sha",
            ZERO_SHA,
            "--head-sha",
            head_sha,
        )

        self.assertEqual(1, result.returncode)
        self.assertIn("Coverage gate failed: changed-module coverage is below threshold.", result.stdout)
        self.assertNotIn("skipped", result.stdout)

    def test_normal_base_sha_compares_only_changed_source_files(self):
        self.add_source_file("Existing.cs")
        base_sha = self.commit("initial")
        self.add_source_file("Changed.cs")
        head_sha = self.commit("changed source")

        result = self.run_checker(
            self.write_coverage("Changed.cs"),
            "--base-sha",
            base_sha,
            "--head-sha",
            head_sha,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("  - adrapi/Changed.cs", result.stdout)
        self.assertNotIn("  - adrapi/Existing.cs", result.stdout)

    def test_invalid_base_sha_fails_without_a_git_traceback(self):
        self.add_source_file("Initial.cs")
        head_sha = self.commit("initial")

        result = self.run_checker(
            self.write_coverage("Initial.cs"),
            "--base-sha",
            "f" * 40,
            "--head-sha",
            head_sha,
        )

        self.assertEqual(1, result.returncode)
        self.assertIn("Coverage gate failed: base commit", result.stdout)
        self.assertNotIn("Traceback", result.stderr)

    def test_unresolvable_base_ref_fails_without_skipping_the_gate(self):
        self.add_source_file("Initial.cs")
        self.commit("initial")

        result = self.run_checker(
            self.write_coverage("Initial.cs"),
            environment={"GITHUB_BASE_REF": "missing"},
        )

        self.assertEqual(1, result.returncode)
        self.assertIn("Coverage gate failed: base ref", result.stdout)
        self.assertNotIn("skipped", result.stdout)

    def test_base_ref_uses_merge_base_for_pull_request_comparison(self):
        self.add_source_file("Existing.cs")
        base_sha = self.commit("initial")
        self.git("branch", "origin/main", base_sha)
        self.add_source_file("Changed.cs")
        head_sha = self.commit("changed source")

        result = self.run_checker(
            self.write_coverage("Changed.cs"),
            environment={"GITHUB_BASE_REF": "main", "GITHUB_SHA": head_sha},
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(f"Diff range: {base_sha}...{head_sha}", result.stdout)

    def test_missing_baseline_skips_only_for_local_runs(self):
        self.add_source_file("Initial.cs")
        self.commit("initial")

        result = self.run_checker(self.write_coverage("Initial.cs"))

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Coverage gate skipped: no base commit provided", result.stdout)


if __name__ == "__main__":
    unittest.main()
