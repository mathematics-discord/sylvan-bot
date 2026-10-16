"""Observe the native Codex summary; never request or execute a review."""

import json
import os
import re
import urllib.request

NAME = "Codex review complete"
BOT_ID = 199175422  # Verified via GitHub's /users/chatgpt-codex-connector[bot].
BOT_LOGIN = "chatgpt-codex-connector[bot]"
SUMMARY = "<!-- codex-pull-request-review-summary -->"
STATE = re.compile(r"<!-- codex-review-state:v1 (\{[^\r\n]*\}) -->")


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key")
        result[key] = value
    return result


def decision(comments, sha):
    summaries = []
    for comment in comments:
        author = comment["user"]
        if (author["id"], author["login"], author["type"]) != (BOT_ID, BOT_LOGIN, "Bot"):
            continue
        body = comment["body"]
        if SUMMARY in body:
            summaries.append(body)
    if len(summaries) != 1:
        return False, "Expected exactly one verified Codex summary; waiting for Code Review."
    body = summaries[0]
    matches = STATE.findall(body)
    if len(matches) != 1 or body.count("codex-review-state:") != 1:
        return False, "Unrecognised Codex state format; cannot establish Code Review completion."
    try:
        state = json.loads(matches[0], object_pairs_hook=unique_object)
        review = state["code_review"]
        commit = review["commit_sha"]
        status = review["status"]
    except (ValueError, KeyError, TypeError):
        return False, "Malformed Codex code_review metadata; waiting for a valid summary."
    if not isinstance(commit, str) or not re.fullmatch(r"[0-9a-f]{40}", commit):
        return False, "Codex Code Review has no recognised full commit SHA."
    if commit != sha:
        return False, "Codex Code Review covers an older/different commit; review this head."
    if status != "completed":
        return False, "Codex Code Review is missing, queued, running, or has an unrecognised status."
    return True, "Native Codex Code Review completed for this exact head; findings do not affect this gate."


def api(method, path, data=None):
    request = urllib.request.Request(
        f"{os.environ['GITHUB_API_URL']}/repos/{os.environ['GITHUB_REPOSITORY']}/{path}",
        data=None if data is None else json.dumps(data).encode(),
        method=method,
        headers={"Authorization": f"Bearer {os.environ['GH_TOKEN']}",
                 "Accept": "application/vnd.github+json",
                 "X-GitHub-Api-Version": "2022-11-28",
                 "Content-Type": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def reconcile(number, request=api):
    pr = request("GET", f"pulls/{number}")
    if pr["base"]["ref"] != "master" or pr["state"] != "open":
        print("PR is not open against master; nothing to reconcile.")
        return
    sha = pr["head"]["sha"]
    if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("GitHub returned an invalid PR head SHA")
    # A new run supersedes earlier evidence, including a previous success.
    check = request("POST", "check-runs", {
        "name": NAME, "head_sha": sha, "status": "in_progress",
        "external_id": f"codex-review-gate:pr:{number}",
        "details_url": f"{os.environ['GITHUB_SERVER_URL']}/{os.environ['GITHUB_REPOSITORY']}/actions/runs/{os.environ['GITHUB_RUN_ID']}",
        "output": {"title": "Reconciling native Codex Code Review", "summary": "Fetching current PR and comment state."},
    })
    path = f"check-runs/{check['id']}"
    try:
        comments = []
        page = 1
        while True:  # Pagination only; no polling or waiting for a review.
            batch = request("GET", f"issues/{number}/comments?per_page=100&page={page}")
            if not isinstance(batch, list):
                raise ValueError("GitHub returned malformed comments")
            comments.extend(batch)
            if len(batch) < 100:
                break
            page += 1
        current = request("GET", f"pulls/{number}")
        if current["head"]["sha"] != sha or current["base"]["ref"] != "master" or current["state"] != "open":
            passed, detail = False, "PR changed during reconciliation; the next event must reconcile its current head."
        else:
            passed, detail = decision(comments, sha)
        payload = {"status": "completed" if passed else "in_progress",
                   "output": {"title": NAME if passed else "Waiting for native Code Review", "summary": detail}}
        if passed:
            payload["conclusion"] = "success"
        request("PATCH", path, payload)
        print(f"PR #{number} head {sha}: {'complete' if passed else 'blocked'}: {detail}")
    except Exception:
        # Failure to fetch/parse/publish cannot leave this run's check successful.
        request("PATCH", path, {"status": "completed", "conclusion": "failure",
                "output": {"title": "Reconciliation failed", "summary": "GitHub API or response error. See workflow logs and manually reconcile after recovery."}})
        raise


def main():
    with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as source:
        event = json.load(source)
    number = (event.get("pull_request", {}).get("number") or
              event.get("issue", {}).get("number") or
              event.get("inputs", {}).get("pr_number"))
    if not re.fullmatch(r"[1-9][0-9]*", str(number)):
        raise ValueError("A positive PR number is required")
    reconcile(int(number))


if __name__ == "__main__":
    main()
