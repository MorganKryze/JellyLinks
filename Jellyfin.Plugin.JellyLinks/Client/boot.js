// JellyLinks client: boot — hooks into the Jellyfin web UI (10.11 and 12, legacy and modern chrome).
(function (JL) {
  'use strict';
  if (JL.booted || typeof ApiClient === 'undefined') { return; }
  JL.booted = true;

  var LINKABLE = ['Movie', 'Series', 'Season', 'Episode'];
  var ID_IN_HASH = /[?&]id=([0-9a-f]{32})/i;

  // The permission is read once per signed-in user; the server re-checks it on every request anyway.
  var allowed = {};
  function canDownload() {
    var uid = ApiClient.getCurrentUserId();
    if (!uid) { return Promise.resolve(false); }
    if (!allowed[uid]) {
      allowed[uid] = ApiClient.getCurrentUser().then(function (u) {
        return !!(u && u.Policy && u.Policy.EnableContentDownloading);
      }, function () { delete allowed[uid]; return false; });
    }
    return allowed[uid];
  }

  function isLinkable(id) {
    return ApiClient.getItem(ApiClient.getCurrentUserId(), id).then(function (item) {
      return LINKABLE.indexOf(item && item.Type) >= 0;
    }, function () { return false; });
  }

  // ---- detail page button ----------------------------------------------------
  // Jellyfin keeps up to 3 cached copies of #itemDetailPage: always work on the one being shown.
  function decorateDetail(view, id) {
    var box = view.querySelector('.mainDetailButtons');
    if (!box || !id) { return; }
    view.setAttribute('data-jl-item', id);
    // A cached view reused for another item: drop the previous item's button before the checks resolve.
    var old = box.querySelector('.jlDetailBtn');
    if (old && old.getAttribute('data-item-id') !== id) { old.remove(); }
    Promise.all([canDownload(), isLinkable(id)]).then(function (r) {
      if (view.getAttribute('data-jl-item') !== id) { return; } // navigated elsewhere meanwhile
      var b = box.querySelector('.jlDetailBtn');
      if (!(r[0] && r[1])) { if (b) { b.remove(); } return; }
      if (!b) {
        b = JL.el('button', {
          type: 'button', className: 'button-flat detailButton emby-button jlDetailBtn', title: 'Liens de téléchargement', attrs: { is: 'emby-button' },
          on: { click: function () { JL.openGenerate([b.getAttribute('data-item-id')]); } }
        }, [JL.el('div', { className: 'detailButton-content' }, [JL.icon('link', 'detailButton-icon')])]);
        box.insertBefore(b, box.querySelector('.btnMoreCommands'));
      }
      b.setAttribute('data-item-id', id);
    });
  }

  // ---- "…" menus and the multi-selection menu --------------------------------
  // The item a menu is about, recorded when it is opened: "…" click, right-click or long-press (contextmenu).
  // The next item sheet consumes it; a record older than MENU_TTL belongs to some other menu.
  var MENU_TTL = 3000;
  var menuItem = null;
  function remember(id) { menuItem = id ? { id: id, at: Date.now() } : null; }
  function takeMenuItem() {
    var m = menuItem;
    menuItem = null;
    return m && Date.now() - m.at <= MENU_TTL ? m.id : null;
  }

  document.addEventListener('click', function (e) {
    var t = e.target && e.target.closest ? e.target.closest('button[data-action="menu"], .btnMoreCommands') : null;
    if (!t) { return; }
    if (t.classList.contains('btnMoreCommands')) {
      var m = ID_IN_HASH.exec(location.hash);
      remember(m && m[1]);
    } else {
      var card = t.closest('[data-id]');
      remember(card && card.getAttribute('data-id'));
    }
  }, true);

  document.addEventListener('contextmenu', function (e) {
    var card = e.target && e.target.closest ? e.target.closest('[data-id]') : null;
    if (card && card.closest('.actionSheet')) { card = null; }
    remember(card && card.getAttribute('data-id'));
  }, true);

  function selectedIds() {
    return Array.prototype.map.call(document.querySelectorAll('.page:not(.hide) .chkItemSelect:checked'), function (i) {
      var c = i.closest('[data-id]');
      return c && c.getAttribute('data-id');
    }).filter(Boolean);
  }

  function decorateSheet(sheet) {
    sheet.setAttribute('data-jl', '1');
    var scroller = sheet.querySelector('.actionSheetScroller');
    var tpl = scroller && scroller.querySelector('.actionSheetMenuItem');
    if (!tpl) { return; }
    var ids = Array.prototype.map.call(scroller.querySelectorAll('.actionSheetMenuItem'), function (b) { return b.getAttribute('data-id'); });
    var isItemMenu = ids.some(function (i) { return i === 'addtoplaylist' || i === 'playlist' || i === 'addtocollection'; });
    if (!isItemMenu) { return; } // sort, filter, playback… sheets
    var multi = !!document.querySelector('.selectionCommandsPanel') && ids.indexOf('selectall') >= 0;
    var single = takeMenuItem();
    if (!multi && !single) { return; }
    canDownload().then(function (ok) {
      if (!ok || !sheet.isConnected) { return; }
      var b = tpl.cloneNode(true);
      b.setAttribute('data-id', 'jellylinks');
      var text = b.querySelector('.actionSheetItemText');
      if (text) { text.textContent = 'Liens de téléchargement'; }
      var ic = b.querySelector('.actionsheetMenuItemIcon');
      if (ic) { ic.className = 'actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons link'; }
      // Do not stop the event: the sheet closes itself and Jellyfin ignores the unknown id.
      b.addEventListener('click', function () {
        var ids2 = multi ? selectedIds() : [single];
        if (ids2.length) { JL.openGenerate(ids2); }
      });
      scroller.appendChild(b);
      keepInView(sheet);
    });
  }

  // Jellyfin places a sheet from its size before our entry exists: pull it back inside the window (its own 20 px margin).
  function keepInView(sheet) {
    var margin = 20;
    var left = parseFloat(sheet.style.left), top = parseFloat(sheet.style.top);
    if (!isNaN(left) && left + sheet.offsetWidth > window.innerWidth - margin) {
      sheet.style.left = Math.max(0, window.innerWidth - sheet.offsetWidth - margin) + 'px';
    }
    if (!isNaN(top) && top + sheet.offsetHeight > window.innerHeight - margin) {
      sheet.style.top = Math.max(0, window.innerHeight - sheet.offsetHeight - margin) + 'px';
    }
  }

  // ---- "Mes liens" entries (drawer, modern user menu, settings page) ---------
  function setLabel(a, text, iconName) {
    var t = a.querySelector('.navMenuOptionText, .listItemBodyText, .MuiListItemText-primary, .MuiTypography-root');
    if (t) { t.textContent = text; } else { a.replaceChildren(text); }
    var ic = a.querySelector('.navMenuOptionIcon, .listItemIcon, .MuiListItemIcon-root');
    if (ic) { ic.replaceWith(JL.icon(iconName, ic.classList.contains('navMenuOptionIcon') ? 'navMenuOptionIcon' : 'listItemIcon')); }
  }

  function openMyLinks(e) {
    if (e) { e.preventDefault(); }
    var backdrop = document.querySelector('#app-user-menu .MuiBackdrop-root');
    if (backdrop) { backdrop.click(); } // close the modern user menu first
    if (JL.openMyLinks) { JL.openMyLinks(); }
  }

  function cloneEntry(tpl, cls) {
    var a = tpl.cloneNode(true);
    a.classList.remove('btnSettings', 'lnkUserProfile');
    a.classList.add(cls);
    a.removeAttribute('data-itemid');
    a.setAttribute('href', '#');
    setLabel(a, 'Mes liens', 'link');
    a.addEventListener('click', openMyLinks);
    tpl.parentNode.insertBefore(a, tpl.nextSibling);
  }

  function decorateNav() {
    if (!JL.openMyLinks) { return; }
    var settings = document.querySelector('.mainDrawer .userMenuOptions .btnSettings');
    if (settings && !settings.parentNode.querySelector('.jlNavLink')) { cloneEntry(settings, 'jlNavLink'); }
    var modern = document.querySelector('#app-user-menu ul.MuiMenu-list a[href="#/mypreferencesmenu"]');
    if (modern && !modern.parentNode.querySelector('.jlNavLink')) { cloneEntry(modern, 'jlNavLink'); }
  }

  function decorateSettings(view) {
    if (!JL.openMyLinks) { return; }
    var profile = view.querySelector('a.lnkUserProfile');
    if (profile && !view.querySelector('.jlSettingsRow')) { cloneEntry(profile, 'jlSettingsRow'); }
  }

  // ---- wiring ----------------------------------------------------------------
  document.addEventListener('viewshow', function (e) {
    var view = e.target;
    var id = e.detail && e.detail.params && e.detail.params.id;
    if (!view) { return; }
    if (view.id === 'itemDetailPage') { decorateDetail(view, id); }
    if (view.id === 'myPreferencesMenuPage') { canDownload().then(function (ok) { if (ok) { decorateSettings(view); } }); }
  });

  var queued = false;
  new MutationObserver(function () {
    if (queued) { return; }
    queued = true;
    requestAnimationFrame(function () {
      queued = false;
      Array.prototype.forEach.call(document.querySelectorAll('.actionSheet:not([data-jl])'), decorateSheet);
      canDownload().then(function (ok) { if (ok) { decorateNav(); } });
    });
  }).observe(document.body, { childList: true, subtree: true });

  // The script may load after the first page was shown.
  var shown = document.querySelector('#itemDetailPage:not(.hide)');
  var m0 = ID_IN_HASH.exec(location.hash);
  if (shown && m0) { decorateDetail(shown, m0[1]); }
  var prefs = document.querySelector('#myPreferencesMenuPage:not(.hide)');
  if (prefs) { canDownload().then(function (ok) { if (ok) { decorateSettings(prefs); } }); }
})(window.JellyLinks);
