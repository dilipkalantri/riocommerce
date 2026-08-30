// Mega-menu helpers: (1) admin nested drag-and-drop tree, (2) storefront flyout edge-flip.
// Vanilla JS, no dependencies. Loaded globally via App.razor.
(function () {
    window.hjc = window.hjc || {};

    function clearIndicators(root) {
        root.querySelectorAll('.mt-drop-before,.mt-drop-after,.mt-drop-inside').forEach(function (el) {
            el.classList.remove('mt-drop-before', 'mt-drop-after', 'mt-drop-inside');
            el._mtZone = null;
        });
    }
    function esc(s) { return (window.CSS && CSS.escape) ? CSS.escape(s) : s; }

    // ── Admin: nested sortable tree via native HTML5 drag-and-drop ──────────────
    // Drag is initiated only from a .mt-handle; the whole .mt-li is the drag unit.
    // On drop we don't move DOM ourselves — we tell .NET (id, targetId, zone) and it
    // mutates the local tree + re-renders. Blazor stays the single source of truth.
    window.hjc.menuTree = {
        init: function (root, dotnetRef) {
            if (!root || root._mtInit) return;
            root._mtInit = true;
            root._mtRef = dotnetRef;
            var dragId = null;

            // Only allow dragging when the grab started on a handle.
            root.addEventListener('mousedown', function (e) {
                var li = e.target.closest('.mt-li');
                if (!li) return;
                li.setAttribute('draggable', e.target.closest('.mt-handle') ? 'true' : 'false');
            });

            root.addEventListener('dragstart', function (e) {
                var li = e.target.closest('.mt-li');
                if (!li) return;
                dragId = li.getAttribute('data-id');
                li.classList.add('mt-dragging');
                e.dataTransfer.effectAllowed = 'move';
                try { e.dataTransfer.setData('text/plain', dragId); } catch (_) { }
            });

            root.addEventListener('dragend', function () {
                var d = root.querySelector('.mt-dragging'); if (d) { d.classList.remove('mt-dragging'); d.setAttribute('draggable', 'false'); }
                clearIndicators(root);
                dragId = null;
            });

            root.addEventListener('dragover', function (e) {
                if (!dragId) return;
                var row = e.target.closest('.mt-row');
                if (!row) return;
                var li = row.closest('.mt-li');
                var targetId = li && li.getAttribute('data-id');
                if (!targetId || targetId === dragId) { clearIndicators(root); return; }
                var draggedLi = root.querySelector('.mt-li[data-id="' + esc(dragId) + '"]');
                if (draggedLi && draggedLi.contains(li)) { clearIndicators(root); return; } // no self-descendant drop
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
                var rect = row.getBoundingClientRect();
                var y = e.clientY - rect.top;
                var zone = y < rect.height * 0.28 ? 'before' : (y > rect.height * 0.72 ? 'after' : 'inside');
                clearIndicators(root);
                row.classList.add('mt-drop-' + zone);
                row._mtZone = zone;
            });

            root.addEventListener('drop', function (e) {
                if (!dragId) return;
                var row = e.target.closest('.mt-row');
                if (!row) return;
                e.preventDefault();
                var li = row.closest('.mt-li');
                var targetId = li && li.getAttribute('data-id');
                var zone = row._mtZone || 'after';
                var dz = dragId;
                clearIndicators(root);
                dragId = null;
                if (targetId && targetId !== dz && root._mtRef) {
                    root._mtRef.invokeMethodAsync('OnMenuDrop', dz, targetId, zone);
                }
            });
        },
        dispose: function (root) { if (root) { root._mtInit = false; root._mtRef = null; } }
    };

    // ── Storefront: keep nested flyouts inside the viewport ─────────────────────
    // Level-2 dropdowns open downward; level-3+ flyouts open to the right, flipping
    // left when there isn't room. Idempotent — safe to call on every render.
    window.hjc.megaFlyout = function () {
        document.querySelectorAll('.hjc-app .hnav-sub:not([data-fly-init])').forEach(function (sub) {
            sub.setAttribute('data-fly-init', '1');
            sub.addEventListener('mouseenter', function () {
                var fly = sub.querySelector(':scope > .hnav-fly');
                if (!fly) return;
                fly.classList.remove('flip-left');
                requestAnimationFrame(function () {
                    var r = fly.getBoundingClientRect();
                    if (r.right > window.innerWidth - 8) fly.classList.add('flip-left');
                });
            });
        });
        document.querySelectorAll('.hjc-app .hnav-item:not([data-drop-init])').forEach(function (item) {
            item.setAttribute('data-drop-init', '1');
            item.addEventListener('mouseenter', function () {
                var drop = item.querySelector(':scope > .hnav-drop');
                if (!drop) return;
                drop.classList.remove('flip-right');
                requestAnimationFrame(function () {
                    var r = drop.getBoundingClientRect();
                    if (r.right > window.innerWidth - 8) drop.classList.add('flip-right');
                });
            });
        });
    };

    // ── Outside-click / Escape dismisser ────────────────────────────────────────
    // Any popover that should close when the user clicks elsewhere. Used by the
    // shared MultiSelect filter.
    //
    // Listens on 'pointerdown' rather than 'click' so the popover is already closed by
    // the time the click lands, and the click still reaches whatever was underneath —
    // a transparent backdrop would swallow it and cost the user a second click.
    //
    // Capture phase, because a handler inside the page may stopPropagation and we would
    // otherwise never hear about the click at all.
    window.hjc.dismiss = {
        on: function (el, dotnetRef, method) {
            if (!el || el._dismissOn) return;
            var handler = function (e) {
                if (el.contains(e.target)) return;      // inside the popover — not a dismissal
                dotnetRef.invokeMethodAsync(method);
            };
            var keyHandler = function (e) {
                if (e.key === 'Escape') dotnetRef.invokeMethodAsync(method);
            };
            el._dismissOn = { handler: handler, keyHandler: keyHandler };
            document.addEventListener('pointerdown', handler, true);
            document.addEventListener('keydown', keyHandler, true);
        },
        off: function (el) {
            if (!el || !el._dismissOn) return;
            document.removeEventListener('pointerdown', el._dismissOn.handler, true);
            document.removeEventListener('keydown', el._dismissOn.keyHandler, true);
            el._dismissOn = null;
        }
    };
})();
