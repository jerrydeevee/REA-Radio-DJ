// Translations for the web pages. The pages are written in Dutch and the Dutch text is the key, as in the studio
// (src/ReaRadioDj.Contracts/Localization/strings.tsv). The language is ?lang=en|de|es|nl, else the last choice, else the
// browser's language. Fixed texts in the page are translated when it loads; scripts use tr() for their own texts and
// trE() for texts the engine sends (always Dutch, often with values filled in).
"use strict";
(function () {
  const langs = ["nl", "en", "de", "es"];
  const pick = (c) => (langs.includes((c || "").slice(0, 2).toLowerCase()) ? (c || "").slice(0, 2).toLowerCase() : null);
  let lang = pick(new URLSearchParams(location.search).get("lang"));
  try {
    if (lang) localStorage.setItem("rea-lang", lang);
    else lang = pick(localStorage.getItem("rea-lang"));
  } catch { /* no storage (private window): the choice is not remembered */ }
  lang = lang || pick(navigator.language) || "nl";
  document.documentElement.lang = lang;

  let map = {};
  if (lang !== "nl") {
    // Synchronous on purpose: the scripts of the page need the texts before they run; the table is small and local.
    try {
      const x = new XMLHttpRequest();
      x.open("GET", `/tr/${lang}.json`, false);
      x.send();
      if (x.status === 200) map = JSON.parse(x.responseText);
    } catch { /* engine too old or unreachable: the page stays Dutch */ }
  }

  const fill = (text, args) => text.replace(/\{(\d+)(?:[,:][^{}]*)?\}/g, (h, n) => (args[n] !== undefined ? String(args[n]) : h));
  const hole = /\{(\d+)(?:[,:][^{}]*)?\}/g;
  const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  let templates = null;
  const build = () => Object.keys(map).filter((k) => /\{\d/.test(k)).map((k) => {
    let re = "^", last = 0, letters = 0;
    const seen = new Set();
    for (const m of k.matchAll(hole)) {
      const part = k.slice(last, m.index);
      re += esc(part);
      letters += (part.match(/\p{L}/gu) || []).length;
      const g = "g" + m[1];
      const final = m.index + m[0].length === k.length;
      re += seen.has(g) ? `\\k<${g}>` : final ? `(?<${g}>.+)` : `(?<${g}>.+?)`;
      seen.add(g);
      last = m.index + m[0].length;
    }
    re += esc(k.slice(last)) + "$";
    letters += (k.slice(last).match(/\p{L}/gu) || []).length;
    return letters >= 3 ? { re: new RegExp(re, "su"), to: map[k], letters } : null;
  }).filter(Boolean).sort((a, b) => b.letters - a.letters);

  /** A text of the page in the chosen language, with {0}, {1}… filled in. */
  window.tr = (nl, ...args) => fill(map[nl] || nl, args);

  /** A text from the engine in the chosen language; anything unknown stays as it is. */
  window.trE = (text) => {
    if (!text || lang === "nl") return text;
    if (map[text]) return map[text];
    templates = templates || build();
    for (const t of templates) {
      const m = t.re.exec(text);
      if (m) return t.to.replace(hole, (h, n) => (m.groups["g" + n] !== undefined ? window.trE(m.groups["g" + n]) : h));
    }
    return text;
  };

  window.trLang = lang;

  // Fixed texts in the page: text nodes and the attributes people read.
  const translateTree = (root) => {
    const walk = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode: (n) => (n.parentElement && !n.parentElement.closest("script,style,[data-notr]") ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT),
    });
    for (let n = walk.nextNode(); n; n = walk.nextNode()) {
      const t = n.nodeValue.trim();
      if (t && map[t]) n.nodeValue = n.nodeValue.replace(t, map[t]);
    }
    for (const el of root.querySelectorAll("[placeholder],[title],[aria-label]")) {
      for (const a of ["placeholder", "title", "aria-label"]) {
        const v = el.getAttribute(a);
        if (v && map[v.trim()]) el.setAttribute(a, map[v.trim()]);
      }
    }
  };
  const translatePage = () => {
    if (map[document.title]) document.title = map[document.title];
    translateTree(document.body);
  };
  if (lang !== "nl") {
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", translatePage);
    else translatePage();
  }
})();
