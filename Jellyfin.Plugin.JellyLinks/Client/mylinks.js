// JellyLinks client: mylinks — the "Mes liens" page (cards, option B), as a dialog reachable from every chrome.
(function (JL) {
  'use strict';
  var el = JL.el;
  var LABELS = { copy: 'Copier', revoke: 'Révoquer', regenerate: 'Régénérer' };

  function link(text, fn) { return el('button', { type: 'button', className: 'jlLink', text: text, on: { click: fn } }); }
  function bar(pct, dim) { return el('div', { className: 'jlBar' + (dim ? ' jlDim' : '') }, [el('i', { attrs: { style: 'width:' + pct + '%' } })]); }

  JL.openMyLinks = function () {
    var d = JL.openDialog('Mes liens');
    var data = null, notice = '', open = {}, busy = false;

    function load() {
      d.content.replaceChildren(el('div', { className: 'jlMsg jlMuted', text: 'Chargement…' }));
      Promise.all([JL.api('GET', 'batches/mine'), JL.api('GET', 'batches/quota')]).then(function (r) {
        data = { batches: r[0], quota: r[1] };
        render();
      }, function (status) { d.content.replaceChildren(el('div', { className: 'jlMsg', text: JL.errorMessage(status) })); });
    }

    function files(b) {
      return el('table', { className: 'jlFiles' }, b.Links.map(function (l) {
        return el('tr', null, [
          el('td', { text: l.FileName }),
          el('td', { text: JL.FILE_STATUS[l.Status] || l.Status }),
          el('td', { text: JL.formatBytes(l.BytesReceived) + ' / ' + JL.formatBytes(l.Size) })
        ]);
      }));
    }

    function card(b) {
      var v = JL.batchView(b);
      var toggle = function () { open[b.Id] = !open[b.Id]; render(); };
      var top = el('div', { className: 'jlCardTop', attrs: { role: 'button', tabindex: '0', 'aria-expanded': String(!!open[b.Id]) },
        on: { click: toggle, keydown: function (e) { if (e.key === 'Enter') { toggle(); } } } },
        [el('b', { text: JL.labelTitle(b.Label) }), el('span', { className: 'jlMuted', text: v.expires })]);
      var line = el('div', null, [el('span', { className: 'jl-' + v.tone, text: v.status })]);
      if (v.note) { line.appendChild(el('span', { className: 'jlMuted', text: ' · ' + v.note })); }
      v.actions.forEach(function (a) {
        line.appendChild(document.createTextNode(' · '));
        line.appendChild(link(LABELS[a], function () { act(a, b); }));
      });
      var c = el('div', { className: 'jlCard' }, [top, el('div', { className: 'jlMuted', text: v.size }), bar(v.pct, v.tone === 'off'), line]);
      if (open[b.Id]) { c.appendChild(files(b)); }
      return c;
    }

    function render() {
      var nodes = [];
      if (notice) { nodes.push(el('div', { className: 'jlMsg', text: notice })); }
      var q = JL.quotaLine(data.quota);
      if (q) { nodes.push(el('div', { className: 'jlMuted', text: q.text }), bar(q.pct)); }
      if (!data.batches.length) {
        nodes.push(el('div', { className: 'jlMsg jlMuted', text: 'Aucun lot pour l’instant : utilise l’icône lien (« Liens de téléchargement ») sur un film ou une série.' }));
      }
      data.batches.forEach(function (b) { nodes.push(card(b)); });
      d.content.replaceChildren.apply(d.content, nodes);
    }

    function act(a, b) {
      if (a === 'copy') {
        JL.copyText(JL.linksText(b)).then(function (ok) {
          if (ok) { notice = 'Liens de « ' + JL.labelTitle(b.Label) + ' » copiés.'; render(); return; }
          d.content.replaceChildren(
            el('div', { className: 'jlMsg', text: 'Copie automatique impossible ici : sélectionne le texte ci-dessous et copie-le.' }),
            el('textarea', { className: 'jlManual', value: JL.linksText(b), attrs: { readonly: '' } }),
            link('◂ retour', render));
        });
        return;
      }
      if (a === 'revoke' && !b.confirmed) {
        d.content.replaceChildren(
          el('div', { className: 'jlMsg', text: 'Révoquer « ' + JL.labelTitle(b.Label) + ' » ? Ses liens cesseront de fonctionner.' }),
          el('div', { className: 'jlTools' }, [
            link('Révoquer', function () { act(a, Object.assign({}, b, { confirmed: true })); }),
            link('Annuler', render)
          ]));
        return;
      }
      if (busy) { return; }
      busy = true;
      JL.api('POST', 'batches/' + b.Id + '/' + a).then(function () {
        busy = false;
        notice = a === 'revoke' ? 'Lot révoqué.' : 'Nouveau lot créé : ses liens sont prêts à être copiés.';
        load();
      }, function (status) { busy = false; notice = JL.errorMessage(status); render(); });
    }

    load();
  };
})(window.JellyLinks);
