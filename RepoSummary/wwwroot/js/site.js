// RepoSummary — small progressive-enhancement helpers. No framework, no build step.
(function () {
  "use strict";

  // ---------- Toasts ----------
  var toast = document.getElementById("app-toast");
  var toastTimer;
  function showToast(msg) {
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add("show");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toast.classList.remove("show"); }, 1800);
  }

  // ---------- Copy to clipboard ----------
  // A .copy-btn copies text from: data-copy (literal), data-copy-target (a selector),
  // or the .copyable element inside its data-copy-closest ancestor.
  function textFor(btn) {
    if (btn.dataset.copy) return btn.dataset.copy;
    if (btn.dataset.copyTarget) {
      var t = document.querySelector(btn.dataset.copyTarget);
      return t ? t.innerText : "";
    }
    if (btn.dataset.copyClosest) {
      var host = btn.closest(btn.dataset.copyClosest);
      if (host) {
        var ta = host.querySelector(".artifact-edit");   // editable output → copy current text
        if (ta) return ta.value;
        var el = host.querySelector(".copyable");
        if (el) return el.innerText;
      }
      return "";
    }
    return "";
  }
  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).catch(function () { legacyCopy(text); });
    }
    legacyCopy(text);
    return Promise.resolve();
  }
  function legacyCopy(text) {
    var ta = document.createElement("textarea");
    ta.value = text; ta.style.position = "fixed"; ta.style.opacity = "0";
    document.body.appendChild(ta); ta.select();
    try { document.execCommand("copy"); } catch (e) {}
    document.body.removeChild(ta);
  }
  document.addEventListener("click", function (e) {
    var btn = e.target.closest(".copy-btn");
    if (!btn) return;
    e.preventDefault();
    var text = (textFor(btn) || "").trim();
    if (!text) { showToast("Nothing to copy"); return; }
    copyText(text).then(function () { showToast("Copied"); });
  });

  // ---------- Submit loading states ----------
  // <form data-loading="Analyzing…"> disables its submit button and shows the label.
  document.querySelectorAll("form[data-loading]").forEach(function (form) {
    form.addEventListener("submit", function () {
      var btn = form.querySelector("button[type=submit], button:not([type])");
      if (!btn || btn.disabled) return;
      btn.dataset.original = btn.textContent;
      btn.classList.add("is-loading");
      btn.textContent = form.dataset.loading || "Working…";
      btn.disabled = true;
      // Let the browser submit, then keep it disabled until navigation.
      setTimeout(function () { btn.disabled = true; }, 0);
    });
  });

  // ---------- Smarter repo input ----------
  // Cleans up pasted GitHub URLs into owner/repo as you type.
  function cleanRepo(v) {
    v = v.trim();
    v = v.replace(/^https?:\/\//i, "").replace(/^www\./i, "");
    v = v.replace(/^github\.com\//i, "");
    v = v.replace(/\.git$/i, "").replace(/\/+$/, "");
    return v;
  }
  document.querySelectorAll("[data-repo-input]").forEach(function (input) {
    input.addEventListener("paste", function () {
      setTimeout(function () { input.value = cleanRepo(input.value); }, 0);
    });
    input.addEventListener("blur", function () {
      if (input.value) input.value = cleanRepo(input.value);
    });
  });

  // ---------- Explorer filter ----------
  document.querySelectorAll(".explorer-search").forEach(function (box) {
    var root = document.querySelector(box.dataset.for || "[data-explorer]");
    if (!root) return;
    box.addEventListener("input", function () {
      var q = box.value.trim().toLowerCase();
      var lis = root.querySelectorAll("li");
      if (!q) { lis.forEach(function (li) { li.classList.remove("search-hide"); }); return; }
      lis.forEach(function (li) { li.classList.add("search-hide"); });
      lis.forEach(function (li) {
        var name = nameOf(li);
        if (name && name.indexOf(q) !== -1) reveal(li, root);
      });
    });
  });
  function nameOf(li) {
    var det = li.querySelector(":scope > details");
    if (det) {
      var n = det.querySelector(":scope > summary > .exp-name");
      return n ? n.textContent.toLowerCase() : "";
    }
    return li.textContent.trim().toLowerCase(); // file row: only the file name has text
  }
  function reveal(li, root) {
    var node = li;
    while (node && node !== root) {
      if (node.tagName === "LI") node.classList.remove("search-hide");
      if (node.tagName === "DETAILS") node.open = true;
      node = node.parentElement;
    }
  }

  // ---------- Tabs (progressive enhancement) ----------
  // Without JS, all panels show (a plain scroll). With JS, [data-tabs] becomes a
  // proper tablist: one panel visible at a time, arrow-key navigable.
  document.querySelectorAll("[data-tabs]").forEach(function (tabs) {
    var nav = tabs.querySelector(":scope > .tabnav");
    var wrap = tabs.querySelector(":scope > .tabpanels");
    if (!nav || !wrap) return;
    var btns = Array.prototype.slice.call(nav.querySelectorAll(":scope > .tab-btn"));
    var panels = Array.prototype.slice.call(wrap.querySelectorAll(":scope > .tabpanel"));
    if (!btns.length) return;

    tabs.classList.add("js-tabs");

    function panelFor(btn) { return panels.filter(function (p) { return p.dataset.panel === btn.dataset.tab; })[0]; }

    function activate(i, focus) {
      btns.forEach(function (b, j) {
        var on = j === i;
        b.setAttribute("aria-selected", on ? "true" : "false");
        b.setAttribute("tabindex", on ? "0" : "-1");
        b.classList.toggle("active", on);
        var p = panelFor(b);
        if (p) p.classList.toggle("active", on);
      });
      if (focus) btns[i].focus();
    }

    btns.forEach(function (btn, i) {
      btn.addEventListener("click", function () { activate(i); });
      btn.addEventListener("keydown", function (e) {
        var last = btns.length - 1;
        if (e.key === "ArrowRight" || e.key === "ArrowDown") { e.preventDefault(); activate(i === last ? 0 : i + 1, true); }
        else if (e.key === "ArrowLeft" || e.key === "ArrowUp") { e.preventDefault(); activate(i === 0 ? last : i - 1, true); }
        else if (e.key === "Home") { e.preventDefault(); activate(0, true); }
        else if (e.key === "End") { e.preventDefault(); activate(last, true); }
      });
    });

    var start = 0;
    for (var k = 0; k < btns.length; k++) {
      if (btns[k].getAttribute("data-active") === "true") { start = k; break; }
    }
    activate(start);
  });

  // ---------- Auto-size editable outputs ----------
  function autosize(ta) { ta.style.height = "auto"; ta.style.height = (ta.scrollHeight + 2) + "px"; }
  document.querySelectorAll("textarea[data-autosize]").forEach(function (ta) {
    autosize(ta);
    ta.addEventListener("input", function () { autosize(ta); });
  });

  // ---------- Edit toggle on generated artifacts (read formatted ↔ edit raw) ----------
  document.addEventListener("click", function (e) {
    var btn = e.target.closest("[data-edit-toggle]");
    if (!btn) return;
    e.preventDefault();
    var art = btn.closest(".artifact");
    if (!art) return;
    var editing = art.classList.toggle("editing");
    btn.textContent = editing ? "Done" : "Edit";
    if (editing) {
      var ta = art.querySelector(".artifact-edit");
      if (ta) { autosize(ta); ta.focus(); }
    }
  });

  // ---------- Click an evidence tag → jump to it in the Evidence tab ----------
  function flash(el) { el.classList.add("flash"); setTimeout(function () { el.classList.remove("flash"); }, 1600); }
  document.addEventListener("click", function (e) {
    var chip = e.target.closest(".evidence-chip");
    if (!chip) return;
    var label = (chip.dataset.evidence || "").toLowerCase().trim();
    if (!label) return;

    var evBtn = document.querySelector('.tab-btn[data-tab="evidence"]');
    if (evBtn) evBtn.click();

    var panel = document.querySelector('.tabpanel[data-panel="evidence"]');
    if (!panel) return;
    var candidates = panel.querySelectorAll(".lang-name, .chip, dd, .commit-row .msg, .commit-row .sha, .signal");
    var match = null;
    candidates.forEach(function (el) {
      if (!match && el.textContent.toLowerCase().indexOf(label) !== -1) match = el;
    });
    if (match) { match.scrollIntoView({ block: "center", behavior: "smooth" }); flash(match); }
  });

  // ---------- Live streaming generation (opt-in per form) ----------
  document.querySelectorAll("form.gen-form").forEach(function (form) {
    form.addEventListener("submit", function (e) {
      var cb = form.querySelector('input[name="GenStream"]');
      if (cb && cb.checked) {                // streaming → handle inline, no reload
        e.preventDefault();
        streamGenerate(form);
        return;
      }
      showGenOverlay();                      // normal full-page POST → obvious loading
    });
  });

  // A clear, animated overlay so a slow AI call doesn't look like a frozen page.
  function showGenOverlay() {
    if (document.querySelector(".gen-overlay")) return;
    var el = document.createElement("div");
    el.className = "gen-overlay";
    el.innerHTML =
      '<div class="gen-overlay-card">' +
      '<div class="spinner"></div>' +
      '<div class="gen-overlay-title">Generating your material…</div>' +
      '<div class="gen-overlay-sub">Writing with AI — usually 10–30 seconds. <span class="gen-elapsed">0s</span></div>' +
      "</div>";
    document.body.appendChild(el);

    var start = Date.now();
    var elapsedEl = el.querySelector(".gen-elapsed");
    var subEl = el.querySelector(".gen-overlay-sub");
    setInterval(function () {
      var s = Math.round((Date.now() - start) / 1000);
      if (elapsedEl) elapsedEl.textContent = s + "s";
      if (s === 45 && subEl) subEl.innerHTML = 'Still working — first runs and larger repos take longer. <span class="gen-elapsed">' + s + "s</span>";
      if (s === 90 && subEl) subEl.innerHTML = 'This is taking unusually long; it may have failed. You can wait a little longer or reload the page. <span class="gen-elapsed">' + s + "s</span>";
    }, 1000);
  }

  function streamGenerate(form) {
    var card = form.closest(".card") || form.parentElement;
    var pane = card.querySelector(".stream-out");
    if (!pane) {
      pane = document.createElement("div");
      pane.className = "stream-out";
      pane.innerHTML = '<button class="copy-btn" type="button" data-copy-closest=".stream-out">Copy</button><pre class="stream-live copyable"></pre>';
      card.appendChild(pane);
    }
    var pre = pane.querySelector(".stream-live");
    pre.textContent = "";
    pane.scrollIntoView({ block: "nearest", behavior: "smooth" });

    var btn = form.querySelector('button[type="submit"]');
    var original = btn ? btn.textContent : "";
    if (btn) { btn.disabled = true; btn.classList.add("is-loading"); btn.textContent = "Streaming…"; }

    fetch("/generate/stream", { method: "POST", body: new FormData(form) })
      .then(function (resp) {
        if (!resp.ok || !resp.body) { pre.textContent = "Streaming failed (" + resp.status + "). Try the normal Generate button."; return; }
        var reader = resp.body.getReader();
        var dec = new TextDecoder();
        function pump() {
          return reader.read().then(function (result) {
            if (result.done) return;
            pre.textContent += dec.decode(result.value, { stream: true });
            return pump();
          });
        }
        return pump();
      })
      .catch(function () { pre.textContent += "\n\n[Streaming interrupted.]"; })
      .finally(function () {
        if (btn) { btn.disabled = false; btn.classList.remove("is-loading"); btn.textContent = original; }
      });
  }

  // ---------- Ask this repo (streamed Q&A) ----------
  function askRepo(form) {
    var input = form.querySelector("[data-ask-input]");
    var q = input ? input.value.trim() : "";
    if (!q) { if (input) input.focus(); return; }

    var card = form.closest(".card");
    var answer = card ? card.querySelector("[data-ask-answer]") : null;
    if (!answer) return;
    answer.hidden = false;
    answer.textContent = "Thinking…";
    answer.scrollIntoView({ block: "nearest", behavior: "smooth" });

    var btn = form.querySelector('button[type="submit"]');
    var original = btn ? btn.textContent : "";
    if (btn) { btn.disabled = true; btn.classList.add("is-loading"); btn.textContent = "Asking…"; }

    fetch("/ask/stream", { method: "POST", body: new FormData(form) })
      .then(function (resp) {
        if (!resp.ok || !resp.body) { answer.textContent = "Couldn't get an answer (" + resp.status + ")."; return; }
        var reader = resp.body.getReader();
        var dec = new TextDecoder();
        var first = true;
        function pump() {
          return reader.read().then(function (result) {
            if (result.done) return;
            if (first) { answer.textContent = ""; first = false; }
            answer.textContent += dec.decode(result.value, { stream: true });
            answer.scrollTop = answer.scrollHeight;
            return pump();
          });
        }
        return pump();
      })
      .catch(function () { answer.textContent += "\n\n[Interrupted.]"; })
      .finally(function () {
        if (btn) { btn.disabled = false; btn.classList.remove("is-loading"); btn.textContent = original; }
      });
  }

  document.addEventListener("submit", function (e) {
    var f = e.target.closest("[data-ask-form]");
    if (!f) return;
    e.preventDefault();
    askRepo(f);
  });
  document.addEventListener("click", function (e) {
    var chip = e.target.closest(".ask-example");
    if (!chip) return;
    e.preventDefault();
    var form = chip.closest(".card").querySelector("[data-ask-form]");
    if (!form) return;
    var input = form.querySelector("[data-ask-input]");
    if (input) input.value = chip.textContent.trim();
    askRepo(form);
  });

  // "Explain this file" — jump to the Ask tab with a preset question, then run it.
  document.addEventListener("click", function (e) {
    var btn = e.target.closest("[data-explain]");
    if (!btn) return;
    e.preventDefault();
    var path = btn.getAttribute("data-explain");
    var askTab = document.querySelector('.tab-btn[data-tab="ask"]');
    if (askTab) askTab.click();
    var form = document.querySelector("[data-ask-form]");
    if (!form) return;
    var input = form.querySelector("[data-ask-input]");
    if (input) input.value = "Explain what " + path + " does and how it works.";
    askRepo(form);
  });

  // ---------- Mermaid architecture diagram (bundled locally; progressive enhancement) ----------
  if (window.mermaid && document.querySelector(".mermaid")) {
    try {
      var dark = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
      window.mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: dark ? "dark" : "default" });
      window.mermaid.run({ querySelector: ".mermaid" });
    } catch (err) { /* leave the Mermaid source visible as a fallback */ }
  }

  // ---------- Print / Save as PDF ([data-print] → the browser print dialog) ----------
  document.addEventListener("click", function (e) {
    var btn = e.target.closest("[data-print]");
    if (!btn) return;
    e.preventDefault();
    window.print();
  });

  // expose for inline use if ever needed
  window.RepoSummary = { showToast: showToast };
})();
