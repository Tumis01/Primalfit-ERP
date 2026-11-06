(function () {
    // Enter selects a combobox option without submitting the surrounding form.
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && event.target.matches('input[role="combobox"]')) event.preventDefault();
    }, true);
    function formatNumberInput(input) {
        if (document.activeElement === input || input.value === '' || input.value === null) return;
        // Never round transaction inputs in the browser. Exchange rates,
        // quantities and item ratios can require more than two decimals;
        // converting them to Number would also lose significant digits.
        var formatted = input.value.replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '');
        if (input.value !== formatted) input.value = formatted;
    }

    function prepare(root) {
        var inputs = Array.from(root.querySelectorAll('input[type="number"]'));
        if (root.matches && root.matches('input[type="number"]')) inputs.unshift(root);
        inputs.forEach(function (input) {
            if (!input.hasAttribute('step')) input.setAttribute('step', '0.0001');
            formatNumberInput(input);
            if (input.dataset.transactionNumberReady) return;
            input.dataset.transactionNumberReady = 'true';
            input.addEventListener('focus', function () { input.dataset.transactionNumberFocused = 'true'; });
            input.addEventListener('blur', function () {
                delete input.dataset.transactionNumberFocused;
                formatNumberInput(input);
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        prepare(document);
        new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node.nodeType === 1) prepare(node);
                });
            });
        }).observe(document.body, { childList: true, subtree: true });
    });
})();

window.primafitScrollToNotice = function (element) { if (element) { element.scrollIntoView({ behavior: "smooth", block: "center" }); element.focus({ preventScroll: true }); } };
