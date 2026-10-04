"""Publish only explicit, verified prerelease setup assets; never log credentials.
Uses an existing noninteractive Git Credential Manager credential in memory.
Default is validation only; --publish is required for GitHub mutation.
"""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request

REPO = "kekeqwq/kikicast"
ROOT = Path(__file__).resolve().parent.parent


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()


def credential():
    result = subprocess.run(["git", "-C", str(ROOT), "-c", "credential.interactive=never", "credential", "fill"],
                            input="protocol=https\nhost=github.com\n\n", text=True, capture_output=True, timeout=30,
                            env={**os.environ, "GIT_TERMINAL_PROMPT": "0", "GCM_INTERACTIVE": "Never"})
    fields = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    if result.returncode or not fields.get("password"):
        raise RuntimeError("No existing noninteractive GitHub credential. Authenticate explicitly; credentials are not fabricated or printed.")
    return fields["password"]


def api(token, method, path, data=None):
    request = urllib.request.Request("https://api.github.com" + path, method=method,
                                    data=None if data is None else json.dumps(data).encode(),
                                    headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json",
                                             "Content-Type": "application/json", "User-Agent": "Kikicast-release"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def upload(token, url, file):
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or parsed.hostname != "uploads.github.com":
        raise RuntimeError("Unexpected GitHub upload endpoint; credential delegation refused.")
    connection = http.client.HTTPSConnection(parsed.hostname, timeout=180)
    try:
        connection.putrequest("POST", parsed.path + "?name=" + urllib.parse.quote(file.name))
        for key, value in {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json", "User-Agent": "Kikicast-release",
                           "Content-Type": "application/octet-stream", "Content-Length": str(file.stat().st_size)}.items():
            connection.putheader(key, value)
        connection.endheaders()
        with file.open("rb") as source:
            while block := source.read(1024 * 1024):
                connection.send(block)
        response = connection.getresponse()
        if response.status != 201:
            raise RuntimeError("GitHub asset upload failed: HTTP " + str(response.status) + "; draft retained.")
        asset = json.load(response)
        with file.open("rb") as source:
            digest = "sha256:" + hashlib.file_digest(source, "sha256").hexdigest()
        if asset.get("size") != file.stat().st_size or asset.get("digest") != digest:
            raise RuntimeError("GitHub stored asset digest/size mismatch; draft retained, not published.")
        return asset
    finally:
        connection.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default="0.1.0-preview.1")
    parser.add_argument("--artifacts", required=True, type=Path)
    parser.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*", args.version):
        parser.error("This publisher supports explicitly labelled prereleases only, not stable-gate bypass.")
    if git("status", "--porcelain") or git("remote", "get-url", "origin") != "https://github.com/" + REPO + ".git":
        raise RuntimeError("Require clean committed source and the exact authorized origin; no force push.")
    source = git("rev-parse", "HEAD")
    tag = "v" + args.version
    if git("rev-parse", tag + "^{commit}") != source:
        raise RuntimeError("Release tag must identify this exact source commit.")
    remote = git("ls-remote", "origin", "refs/tags/" + tag + "^{}")
    if not remote.startswith(source + "\t"):
        raise RuntimeError("Exact annotated source tag must already be on GitHub.")
    directory = args.artifacts.resolve(strict=True)
    receipts = []
    installers = []
    for arch, runtime in [("x86_64", "win-x64"), ("aarch64", "win-arm64")]:
        file = directory / f"Kikicast-{args.version}-windows-{arch}-setup.exe"
        if not file.is_file() or file.is_symlink() or not 1024 * 1024 < file.stat().st_size < 512 * 1024 * 1024:
            raise RuntimeError("Missing/unsafe/bounded installer asset: " + file.name)
        with file.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if (file.with_name(file.name + ".sha256")).read_text().strip().lower() != digest + "  " + file.name.lower():
            raise RuntimeError("Installer checksum sidecar mismatch.")
        payload = json.loads(file.with_name(file.name + ".json").read_text(encoding="utf-8-sig"))
        if payload["sourceRevision"] != source or payload["version"] != args.version or payload["runtime"] != runtime or not payload["preview"] or not payload["selfContained"]:
            raise RuntimeError("Installer/source/architecture/preview manifest mismatch.")
        evidence = json.loads((directory / (file.name + ".evidence") / "installer" / "installer-evidence.json").read_text(encoding="utf-8-sig"))
        for field in ["privateAppId", "identicalPayload", "perUserInstall", "startMenu", "sameIdentityUpgrade", "uninstall", "installedTraySmoke", "installedModelsSmoke"]:
            if not evidence[field]:
                raise RuntimeError("Incomplete owned installer acceptance.")
        receipts.append({"asset": file.name, "runtime": runtime, "sha256": digest, "sourceRevision": source,
                         "validation": payload["publishedValidation"], "ownedInstallUpgradeUninstall": True,
                         "installerScope": "same payload/recipe, private identity/mutex/group; not unassisted real-profile acceptance",
                         "signed": False, "nativeX64Accepted": False, "stableReadiness": False})
        installers.append(file)
    if not args.publish:
        print("PASS: local source/tag/dual setup/manifest/checksum/owned installer validation only; no metadata overwrite or GitHub release created.")
        return
    sums = directory / "SHA256SUMS.txt"
    receipt = directory / "release-validation.json"
    if sums.exists() or receipt.exists():
        raise RuntimeError("Release metadata already exists; no silent overwrite/retry publication.")
    sums.write_text("".join(r["sha256"] + "  " + r["asset"] + "\n" for r in receipts), encoding="utf-8")
    receipt.write_text(json.dumps({"version": args.version, "sourceRevision": source, "preview": True, "assets": receipts}, indent=2) + "\n", encoding="utf-8")
    token = credential()
    user = api(token, "GET", "/user")
    repository = api(token, "GET", "/repos/" + REPO)
    if user["login"] != "kekeqwq" or not repository.get("permissions", {}).get("push"):
        raise RuntimeError("Unexpected authenticated owner or missing publication permission.")
    try:
        api(token, "GET", "/repos/" + REPO + "/releases/tags/" + tag)
        raise RuntimeError("Release/tag already has a release; no replacement or asset overwrite.")
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
    body = (ROOT / "docs/releases/" / (args.version + ".md")).read_text(encoding="utf-8")
    release = api(token, "POST", "/repos/" + REPO + "/releases", {"tag_name": tag, "target_commitish": source,
                  "name": "Kikicast " + args.version + " — Windows Setup (x86_64 / aarch64)", "body": body, "draft": True, "prerelease": True})
    for file in installers + [sums, receipt]:
        upload(token, release["upload_url"].split("{", 1)[0], file)
    assets = api(token, "GET", "/repos/" + REPO + "/releases/" + str(release["id"]) + "/assets")
    if {a["name"] for a in assets} != {f.name for f in installers + [sums, receipt]}:
        raise RuntimeError("Unexpected final asset set; draft retained.")
    public = api(token, "PATCH", "/repos/" + REPO + "/releases/" + str(release["id"]), {"draft": False, "prerelease": True, "make_latest": "false"})
    print("PASS: published prerelease with verified dual Setup.exe assets: " + public["html_url"])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Never stringify credential-bearing HTTP requests, subprocess output or responses.
        print("Release stopped:", type(error).__name__, str(error) if isinstance(error, RuntimeError) else "see local validation; no credentials printed")
        raise SystemExit(1)
