export default {
  extends: ["@commitlint/config-conventional"],
  rules: {
    "type-enum": [
      2,
      "always",
      [
        "feat",
        "fix",
        "perf",
        "docs",
        "chore",
        "refactor",
        "test",
        "ci",
        "build",
        "deps",
      ],
    ],
    "header-max-length": [1, "always", 120],
    "body-max-line-length": [0, "always"],
    "footer-max-line-length": [0, "always"],
    "subject-case": [0, "always"],
  },
  ignores: [(commit) => /^Signed-off-by: dependabot\[bot\]/m.test(commit)],
};
