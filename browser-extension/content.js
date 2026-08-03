// RepoSummary browser extension — injects an "Analyze in RepoSummary" button on
// GitHub repository pages that deep-links into your local RepoSummary app.
(function () {
  "use strict";

  // The host where your RepoSummary app runs. Edit this to match your setup.
  var HOST = "http://localhost:5080";

  // Top-level GitHub paths that are NOT user/repo pages.
  var RESERVED = [
    "orgs", "settings", "notifications", "explore", "topics", "marketplace",
    "sponsors", "new", "login", "join", "about", "pulls", "issues", "codespaces",
    "features", "pricing", "search", "apps", "collections", "trending", "dashboard"
  ];

  function repoFromPath() {
    var parts = location.pathname.split("/").filter(Boolean);
    if (parts.length < 2) return null;
    if (RESERVED.indexOf(parts[0].toLowerCase()) !== -1) return null;
    return { owner: parts[0], repo: parts[1] };
  }

  function inject() {
    var r = repoFromPath();
    var existing = document.getElementById("reposummary-btn");
    if (!r) { if (existing) existing.remove(); return; }
    if (existing) {
      existing.href = link(r);
      return;
    }
    var a = document.createElement("a");
    a.id = "reposummary-btn";
    a.textContent = "Analyze in RepoSummary";
    a.href = link(r);
    a.target = "_blank";
    a.rel = "noopener";
    a.style.cssText =
      "position:fixed;right:16px;bottom:16px;z-index:9999;background:#1f883d;color:#fff;" +
      "padding:8px 14px;border-radius:8px;font:600 13px system-ui,-apple-system,sans-serif;" +
      "text-decoration:none;box-shadow:0 2px 10px rgba(0,0,0,.28)";
    document.body.appendChild(a);
  }

  function link(r) {
    return HOST + "/Analysis?owner=" + encodeURIComponent(r.owner) + "&repo=" + encodeURIComponent(r.repo);
  }

  inject();

  // GitHub navigates with pjax (no full reload) — re-check when the URL changes.
  var last = location.href;
  setInterval(function () {
    if (location.href !== last) { last = location.href; inject(); }
  }, 800);
})();
