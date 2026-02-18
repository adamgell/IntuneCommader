# Pull Request Quick Reference

## Current Open PRs (9)

### 🔴 P1 - Critical (Merge First)
- **#15** - refactor(auth): remove deferred Certificate/ManagedIdentity auth options
  - Status: ✅ Ready to merge
  - Risk: Low (has tests, no blockers)

### 🟡 P2 - Important (Merge Soon) 
All are documentation-only fixes, can be merged in any order:
- **#16** - docs: reconcile README and ARCHITECTURE.md with implementation
- **#19** - docs: fix 8 accuracy issues from PR review
- **#22** - docs: correct dependency management section
- **#23** - docs: align encryption decision with DataProtection

### 🟢 P3 - Enhancement (Optional)
Form a cohesive set, #20 and #21 depend on #17:
- **#17** - feat(arch): add ICategoryLoader abstraction
  - Status: ⏸️ Needs architectural decision
  - Decision needed: Where should cache-read logic live?
- **#20** - Explain ICategoryLoader cache-read options (blocked by #17)
- **#21** - Add FormatError callback to CategoryLoadContext (blocked by #17)

### 📋 Meta
- **#24** - This PR: Organize and label pull requests

## Recommended Actions

1. **Immediate**: Merge PR #15 (critical auth fix)
2. **This Week**: Review and merge PRs #16, #19, #22, #23 (documentation)
3. **Decision Needed**: Review PR #17 and decide on cache architecture
4. **After Decision**: Merge PRs #20 and #21 based on #17 decision

## Resources

- **Full Details**: See [PR_STATUS.md](../PR_STATUS.md)
- **Contributing Guide**: See [CONTRIBUTING.md](../CONTRIBUTING.md)
- **Wave Tracking**: See [docs/issues/README.md](../docs/issues/README.md)

## Label Suggestions

Consider creating these GitHub labels:

**Priority:**
- `priority: critical` (P1)
- `priority: high` (P2)
- `priority: medium` (P3)

**Type:**
- `type: documentation`
- `type: refactor`
- `type: feature`
- `type: bugfix`

**Status:**
- `status: needs-review`
- `status: needs-decision`
- `status: blocked`
- `status: ready-to-merge`
