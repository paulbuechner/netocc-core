---
name: release
description: Release NetOcc to nuget.org. Prepare the version and CHANGELOG.md, run the local checks, tag, then tell the user the exact push commands and follow the GitHub Actions run until the packages and the GitHub release are out.
disable-model-invocation: true
argument-hint: "[version, e.g. 0.1.0 or 0.2.0-beta.1]"
arguments: [version]
---

# Release NetOcc $version

A pushed tag `v<version>` runs `.github/workflows/build.yml`:
1. natives on four runners;
2. pack;
3. package tests on three OSes;
4. publish: nuget.org via Trusted Publishing, and a GitHub release with the version's CHANGELOG.md section.

You prepare and verify. **The user runs every push**, because your pushes are blocked. Never enter a PIN, never bypass signing; if signing fails, stop and ask.

## 1. One-time setup

Before the first release, ask whether this is done, and walk the user through what isn't:

- **nuget.org:** *username > Trusted Publishing > Create*:
  - package owner `paulbuechner`, provider GitHub Actions;
  - repository owner `paulbuechner`, repository `netocc-core`;
  - workflow file `build.yml` (the name only), environment `release`;
  - scope Push, "new packages and package versions" (after the first release, "only new package versions" is enough);
  - packages `NetOcc` and `NetOcc.runtime.*`, one per line.
- **GitHub variable:** *Settings > Secrets and variables > Actions > Variables*, `NUGET_USER` = the nuget.org profile name (not the email).
- **Optional:** *Settings > Environments > release* with required reviewers, so publishing waits for an approval.

## 2. Version

- `$version` is SemVer 2 without a `v`. It must be higher than the last tag (`git tag --list "v*" --sort=-v:refname`).
- Never reuse a version that reached nuget.org: versions there are immutable, and the only fallback is unlisting.
- No version given: propose one from `[Unreleased]` and ask. Before 1.0, breaking changes bump the minor version, everything else the patch version.

## 3. CHANGELOG.md

- `[Unreleased]` must describe the release for package users: added, changed, fixed, removed. Leave out internal refactors. Settle the wording with the user.
- Rename it to `## [$version] - <today, YYYY-MM-DD>` and put a new empty `## [Unreleased]` above it.
- Update the link definitions at the bottom:
  - `[Unreleased]` compares `v$version...HEAD`;
  - `[$version]` compares the previous tag with `v$version`, or points at `releases/tag/v$version` for the first release.
- Check the notes with `python build.py changelog --version $version`.

## 4. Local checks (Windows)

- **Build and test:** `python build.py generate`, `native --triplet x64-windows` and `x86-windows` if any `.i` changed, then `test --arch x64` and `test --arch x86`. Logs go to `log/`.
- **Package:** `python build.py pack --rids win-x64,win-x86 --version $version`, then `test-package --arch x64` and `--arch x86`.
- **Generated files:** if the netocc-generator changed, run `generate --check` there; it must report up to date.
- Report the results and stop on any failure.

## 5. Commit and tag

- `main` is one amended commit. A tag pins that commit, and the next amend rewrites `main` away from it. At the first release, ask whether `main` should move to normal commits from then on.
- Until the user answers, amend: `git commit --amend --no-edit`.
- Tag, signed and annotated: `git tag -s v$version -m "NetOcc $version"`.

## 6. The user pushes

Give the two commands as separate blocks, in this order:

```bash
git push --force-with-lease origin main
```

```bash
git push origin v$version
```

The tag push starts the release run.

## 7. Follow the run

- Find it with `gh run list --workflow build.yml --limit 3` and follow it with `gh run watch <id>`. For failures, `gh run view <id> --log-failed`.
- On a cold vcpkg cache, OCCT takes 1 to 3 hours per native job; with the cache, minutes.
- If the `release` environment has reviewers, tell the user to approve the publish job.
- **A failed run before publish:** fix it, then have the user delete the tag with `git push origin :refs/tags/v$version`. Delete it locally with `git tag -d v$version`, retag, and push again.
- **Publish already pushed some packages:** use a new version.

## 8. Verify

- The packages are listed on nuget.org: `https://www.nuget.org/packages/NetOcc/$version`, plus the four `NetOcc.runtime.<rid>` packages. Validation and indexing take up to an hour.
- The release exists: `gh release view v$version --repo paulbuechner/netocc-core`. Its notes are the CHANGELOG section and its assets the packages.
- Tell the user what was published, and link both.
