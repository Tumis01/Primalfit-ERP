const dialogs = new Map();
const focusable = element => [...element.querySelectorAll('button:not(:disabled), input:not(:disabled):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex="0"]')]
    .filter(node => !node.hidden && node.getClientRects().length && !node.closest('[inert]'));

export function openDialog(id, element, receiver) {
    if (!element?.isConnected || dialogs.has(id)) return;
    const state = { element, previous: document.activeElement, overflow: document.body.style.overflow, inert: [] };
    let branch = element.parentElement;
    while (branch?.parentElement) {
        for (const sibling of branch.parentElement.children) {
            if (sibling !== branch && sibling instanceof HTMLElement) {
                state.inert.push([sibling, sibling.inert]); sibling.inert = true;
            }
        }
        if (branch.parentElement === document.body) break;
        branch = branch.parentElement;
    }
    state.keydown = event => {
        if (event.key === 'Escape') {
            event.preventDefault(); event.stopPropagation();
            if (element.dataset.busy !== 'true') receiver.invokeMethodAsync('CloseFromKeyboard').catch(() => {});
        } else if (event.key === 'Tab') {
            const nodes = focusable(element), first = nodes[0], last = nodes[nodes.length - 1];
            if (!first) { event.preventDefault(); element.focus(); }
            else if (event.shiftKey && (document.activeElement === first || !element.contains(document.activeElement))) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && (document.activeElement === last || !element.contains(document.activeElement))) { event.preventDefault(); first.focus(); }
        }
    };
    element.addEventListener('keydown', state.keydown);
    dialogs.set(id, state); document.body.style.overflow = 'hidden';
    const initial = element.querySelector('input:not(:disabled):not([type="checkbox"]):not([type="hidden"]), select:not(:disabled), textarea:not(:disabled)') || focusable(element)[0] || element;
    initial.focus();
}

export function closeDialog(id) {
    const state = dialogs.get(id); if (!state) return;
    state.element.removeEventListener('keydown', state.keydown);
    state.inert.forEach(([element, inert]) => { element.inert = inert; });
    document.body.style.overflow = state.overflow; dialogs.delete(id);
    const previous = state.previous;
    const target = previous?.isConnected && !previous.disabled ? previous
        : document.querySelector('[data-dimension-primary]:not(:disabled)') || document.querySelector('[data-dimension-heading]');
    target?.focus({ preventScroll: true });
}

export function focusInvalid(id) {
    const element = dialogs.get(id)?.element;
    const target = element?.querySelector('[aria-invalid="true"], .invalid') || element?.querySelector('[data-form-error]');
    target?.focus();
}
