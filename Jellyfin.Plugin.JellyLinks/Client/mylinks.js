// JellyLinks client: mylinks — "My links": active links first, then the history, as a dialog reachable from every chrome.
(function (JL) {
  'use strict';
  var el = JL.el, t = JL.t;
  var PAGE = 20;
  var ICONS = { copy: 'content_copy', revoke: 'link_off', regenerate: 'refresh' };

  function bar(pct, cls) {
    return el('div', { className: 'jlBar' + (cls ? ' ' + cls : ''), attrs: { role: 'progressbar', 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(pct) } },
      [el('i', { attrs: { style: 'width:' + pct + '%' } })]);
  }

  function goHome() {
    if (window.Emby && Emby.Page && typeof Emby.Page.goHome === 'function') { Emby.Page.goHome(); } else { location.hash = '#/home'; }
  }

  JL.openMyLinks = function () {
    var d = JL.openDialog(t('mine.title'));
    var data = null, open = {}, shown = { active: PAGE, history: PAGE }, historyOpen = null, busy = false;

    function load() {
      d.content.replaceChildren(JL.spinner());
      Promise.all([JL.api('GET', 'batches/mine'), JL.api('GET', 'batches/quota')]).then(function (r) {
        data = { batches: r[0], quota: r[1] };
        render();
      }, function (err) {
        d.content.replaceChildren(
          el('div', { className: 'jlAlert jl-bad', text: JL.errorText(err), attrs: { role: 'alert' } }),
          el('div', { className: 'jlActions' }, [JL.button(t('retry'), 'submit', load)]));
      });
    }

    function quota() {
      var q = JL.quotaLine(data.quota);
      if (!q) { return null; }
      return el('div', { className: 'jlSticky' }, [
        el('div', { className: 'fieldDescription' }, [q.warn ? JL.icon('warning') : null, q.text]),
        bar(q.pct, q.warn ? 'jlWarn' : '')
      ]);
    }

    function files(b) {
      var body = b.Links.map(function (l) {
        return el('tr', null, [
          el('td', { attrs: { 'data-label': t('col.file') } }, [el('span', { className: 'jlEllipsis', text: l.FileName, title: l.FileName })]),
          el('td', { attrs: { 'data-label': t('col.status') }, text: JL.fileStatus(l, b.State) }),
          el('td', { className: 'jlNum', attrs: { 'data-label': t('col.received') }, text: JL.formatBytes(l.BytesReceived) + ' / ' + JL.formatBytes(l.Size) })
        ]);
      });
      return el('table', { className: 'jlTable' }, [el('tbody', null, body)]);
    }

    function card(b) {
      var v = JL.batchView(b), isOpen = !!open[b.Id];
      var head = el('button', { type: 'button', className: 'jlCardHead', attrs: { 'aria-expanded': String(isOpen) },
        on: { click: function () { open[b.Id] = !isOpen; render(); } } },
        [el('span', { className: 'jlTitle jlEllipsis', text: v.title }), JL.icon(isOpen ? 'expand_less' : 'expand_more')]);
      var state = v.icon
        ? el('span', { className: 'jlPill jl-' + v.tone }, [JL.icon(v.icon), v.status])
        : el('span', { className: 'fieldDescription', text: v.status });
      var nodes = [head, el('div', { className: 'fieldDescription', text: v.meta + ' · ' + v.size })];
      if (b.State === 'active' || b.State === 'expired') { nodes.push(bar(v.pct, b.State === 'expired' ? 'jlBarOff' : '')); }
      nodes.push(el('div', null, [state]));
      if (v.actions.length) {
        nodes.push(el('div', { className: 'jlActions' }, v.actions.map(function (a) {
          return JL.button(t('act.' + a), 'flat', function () { act(a, b); }, ICONS[a]);
        })));
      }
      if (isOpen) { nodes.push(files(b)); }
      return el('div', { className: 'paperList jlCard' }, nodes);
    }

    function section(name, list) {
      var nodes = list.slice(0, shown[name]).map(card);
      if (list.length > shown[name]) {
        nodes.push(JL.button(t('mine.more'), 'flat', function () { shown[name] += PAGE; render(); }, 'expand_more'));
      }
      return nodes;
    }

    function empty() {
      return el('div', { className: 'jlState' }, [
        JL.icon('link'),
        el('h3', { text: t('mine.empty') }),
        el('p', { className: 'fieldDescription', text: t('mine.emptyHint') }),
        JL.button(t('mine.browse'), 'submit', function () { d.close(); goHome(); })
      ]);
    }

    function render() {
      var nodes = [], q = quota();
      if (q) { nodes.push(q); }
      if (!data.batches.length) {
        nodes.push(empty());
        d.content.replaceChildren.apply(d.content, nodes);
        return;
      }
      var active = data.batches.filter(function (b) { return b.State === 'active'; });
      var history = data.batches.filter(function (b) { return b.State !== 'active'; });
      if (historyOpen === null) { historyOpen = active.length === 0; }
      if (active.length) {
        nodes.push(el('h3', { className: 'jlSection', text: t('mine.active') }));
        nodes = nodes.concat(section('active', active));
      }
      if (history.length) {
        nodes.push(el('button', { type: 'button', className: 'jlDisclose jlSection', attrs: { 'aria-expanded': String(historyOpen) },
          on: { click: function () { historyOpen = !historyOpen; render(); } } },
          [el('span', { text: t('mine.history') + ' (' + history.length + ')' }), JL.icon(historyOpen ? 'expand_less' : 'expand_more')]));
        if (historyOpen) { nodes = nodes.concat(section('history', history)); }
      }
      d.content.replaceChildren.apply(d.content, nodes);
    }

    function manualCopy(b) {
      var ta = el('textarea', { className: 'jlManual', value: JL.linksText(b), attrs: { readonly: '', 'aria-label': JL.batchTitle(b) } });
      d.content.replaceChildren(el('p', { text: t('gen.manual') }), ta, el('div', { className: 'jlActions' }, [JL.button(t('back'), 'flat', render, 'arrow_back')]));
      ta.focus();
      ta.select();
    }

    function act(a, b) {
      if (a === 'copy') {
        JL.copyText(JL.linksText(b)).then(function (ok) {
          if (ok) { JL.toast(t('gen.copied', { n: b.Links.length })); } else { manualCopy(b); }
        });
        return;
      }
      if (a === 'revoke') {
        JL.confirm({ title: t('mine.revokeQ', { title: JL.batchTitle(b) }), text: t('mine.revokeText'), ok: t('act.revoke'), danger: true })
          .then(function (yes) { if (yes) { post(a, b); } });
        return;
      }
      post(a, b);
    }

    // Only the card that changed moves: the list is re-rendered from the data in hand, never reloaded.
    function post(a, b) {
      if (busy) { return; }
      busy = true;
      JL.api('POST', 'batches/' + b.Id + '/' + a).then(function (fresh) {
        busy = false;
        if (a === 'revoke') {
          b.State = 'revoked';
          b.Links.forEach(function (l) { l.Url = ''; });
          JL.toast(t('mine.revoked'));
        } else {
          data.batches.unshift(fresh);
          JL.toast(t('mine.regenerated'));
        }
        render();
      }, function (err) { busy = false; JL.toast(JL.errorText(err)); });
    }

    load();
  };
})(window.JellyLinks);
