(function () {
    function formatNumberInput(input) {
        if (document.activeElement === input || input.value === '' || input.value === null) return;
        var value = Number(input.value);
        if (Number.isFinite(value)) {
            var formatted = value.toFixed(2);
            if (input.value !== formatted) input.value = formatted;
        }
    }

    function prepare(root) {
        root.querySelectorAll('input[type="number"]').forEach(function (input) {
            if (!input.hasAttribute('step')) input.setAttribute('step', '0.0001');
            formatNumberInput(input);
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
