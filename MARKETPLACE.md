# Publishing Tabkeeper to the Visual Studio Marketplace

The GitHub releases (`.vsix` + SHA-256 checksum) already work and are enough for manual
install. Listing on the Marketplace gets Tabkeeper into **Extensions > Manage
Extensions** search and update notifications, but it is not required.

The `.vsix` is intentionally unsigned. Authenticode-signing with a self-signed
certificate was tried first (to avoid the "Unknown Publisher" warning on direct
install), but the Marketplace rejects VSIX uploads signed with a certificate that
doesn't chain to a trusted root CA ("The certificate for the VSIX package is
invalid"), and a real trusted code-signing certificate is not worth the cost/hassle
here for a single publish target. The SHA-256 checksum on each release is the
integrity check instead.

## Current plan: manual publish, no PAT

Automating publishing from CI needs an Azure DevOps personal access token, which in
turn needs an Azure DevOps organization. Creating one got stuck (the "Continue" button
on org creation silently does nothing for this account - tried incognito, a fresh org
name, and console showed no errors). Chasing it further wasn't worth it, so the plan
for now is manual publishing, which needs none of that:

1. Build a release as usual (already automated by `release.yml`).
2. Go to https://marketplace.visualstudio.com/manage/publishers and sign in as the
   publisher account.
3. Drag in the `.vsix` from the GitHub release. Extension detail fields (display name,
   version, VSIX ID, icon, tags, categories) autopopulate from
   `source.extension.vsixmanifest`; **Overview** can be pasted from
   `Tabkeeper.Vsix/overview.md`.
4. **Save & Upload**, then right-click the extension and **Make Public**.
5. For updates: **Edit** the existing listing, upload the new `.vsix` (some fields are
   read-only after the first publish), **Save & Upload**, **Make Public** again if
   needed.

This is a few minutes of manual work per release, which is fine at this project's
release cadence.

## If automated CI publishing is revisited later

The pieces are in place for it and were previously verified against current Microsoft
Learn docs, but were not able to get a PAT actually created:

- `Tabkeeper.Vsix/publish.json` - listing manifest for `VsixPublisher.exe`, already
  written and matches the required schema.
- `Tabkeeper.Vsix/overview.md` - long-form listing description, already written.

There is currently **no** publish step in `.github/workflows/release.yml` - one was
added and then removed. GitHub Actions does not allow the `secrets` context in a step
`if:` condition (`Unrecognized named-value: 'secrets'`), and since there's no PAT to
gate on anyway, it was simpler to drop the step entirely rather than work around that.

To pick this back up: create an Azure DevOps organization (`https://aex.dev.azure.com/`
or `https://dev.azure.com/`), then user icon (top right) -> **Personal access tokens**
-> **+ New Token** -> organization **All accessible organizations** -> scope
**Marketplace (publish)** (not "Acquire/Manage" - the scope name in the current UI is
"Marketplace (publish)"). Add the token as a GitHub Actions secret named
`VS_MARKETPLACE_PAT`, then re-add a step to `release.yml` after "Publish GitHub
release" - something like:

```yaml
    env:
      VS_MARKETPLACE_PAT: ${{ secrets.VS_MARKETPLACE_PAT }}
    steps:
      ...
      - name: Publish to VS Marketplace
        if: ${{ env.VS_MARKETPLACE_PAT != '' }}
        shell: pwsh
        run: |
          $publisher = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.vssdk.buildtools" -Recurse -Filter VsixPublisher.exe | Select-Object -First 1
          $vsix = Get-ChildItem Tabkeeper.Vsix/bin/Release -Recurse -Filter *.vsix | Select-Object -First 1
          & $publisher.FullName publish -payload $vsix.FullName -publishManifest Tabkeeper.Vsix/publish.json -personalAccessToken $env:VS_MARKETPLACE_PAT
```

Note the `env:` must be at job level (not step level) for the `if:` to see it - the
`env` context in a step's `if:` only sees workflow/job-level env, since step-level env
isn't populated until the step actually runs.

Note Microsoft is retiring **global** (all-accessible-organizations) PATs on
2026-12-01, pushing toward Microsoft Entra ID service-principal auth instead. If this
is picked up again after that date, look at ["Publish with a Microsoft Entra
token"](https://learn.microsoft.com/en-us/azure/devops/extend/publish/command-line?view=azure-devops#publish-with-a-microsoft-entra-token)
instead of minting a PAT.

## Manifest (done)

`Tabkeeper.Vsix/source.extension.vsixmanifest` has `<License>`, `<MoreInfo>`,
`<GettingStartedGuide>`, and `<ReleaseNotes>`. Element order inside `<Metadata>`
matters for VSSDK1062 validation; the working order is:

```
Identity, DisplayName, Description, MoreInfo, License, GettingStartedGuide,
ReleaseNotes, Icon, PreviewImage, Tags, Categories
```

The license file ships via `Tabkeeper.Vsix.csproj`:
`<Content Include="..\LICENSE" Link="LICENSE" IncludeInVSIX="true" />`, and
`<License>` in the manifest must match the packaged name (`LICENSE`, not `..\LICENSE`)
or the build fails with VSSDK1310.

## Still missing (either path)

- [ ] Confirm the publisher ID (`KirkTolleshaug` in the manifest and `publish.json`)
      exactly matches the real Marketplace publisher account
- [x] Screenshot linked from `overview.md` -
      `Tabkeeper.Vsix/Resources/Screenshot-PinnedTabs.png` (pinned vs. active tab in
      the tab strip). Good enough to ship with; a fuller shot (branch name visible,
      more context) can replace it later if wanted.

## Answer to "would I still build in GitHub?"

Yes either way. The Marketplace only hosts the listing and the `.vsix` file; it does
not build anything. CI still builds, tests, and produces the `.vsix`; publishing
(manual or automated) just uploads that same artifact to the Marketplace in addition to
the GitHub release.
