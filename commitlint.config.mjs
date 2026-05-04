export default {
  extends: ['@commitlint/config-conventional'],
  ignores: [(commit) => /^Signed-off-by: dependabot\[bot\]/m.test(commit)]
};
