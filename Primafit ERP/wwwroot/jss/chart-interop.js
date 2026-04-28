window.erpCharts = {
    // Keep track of chart instances so we can destroy them before re-rendering (prevents hover glitches)
    instances: {},

    renderCashFlowChart: function (canvasId, labels, revenueData, expenseData, profitData) {
        if (this.instances[canvasId]) this.instances[canvasId].destroy();

        const ctx = document.getElementById(canvasId).getContext('2d');
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Revenue',
                        data: revenueData,
                        backgroundColor: 'rgba(16, 185, 129, 0.8)', // Emerald
                        order: 2
                    },
                    {
                        label: 'Expenses',
                        data: expenseData,
                        backgroundColor: 'rgba(244, 63, 94, 0.8)', // Rose
                        order: 3
                    },
                    {
                        label: 'Net Profit',
                        data: profitData,
                        type: 'line',
                        borderColor: 'rgba(99, 102, 241, 1)', // Indigo
                        borderWidth: 3,
                        tension: 0.4,
                        fill: false,
                        order: 1
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'top' } }
            }
        });
    },

    renderAgingChart: function (canvasId, arData, apData) {
        if (this.instances[canvasId]) this.instances[canvasId].destroy();

        const ctx = document.getElementById(canvasId).getContext('2d');
        this.instances[canvasId] = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: ['Current', '1-30 Days', '31-60 Days', 'Over 60 Days'],
                datasets: [
                    {
                        label: 'Accounts Receivable (Owed to You)',
                        data: arData,
                        backgroundColor: 'rgba(16, 185, 129, 0.8)', // Emerald
                    },
                    {
                        label: 'Accounts Payable (You Owe)',
                        data: apData,
                        backgroundColor: 'rgba(244, 63, 94, 0.8)', // Rose
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'bottom' } }
            }
        });
    },

    renderTopExpensesChart: function (canvasId, labels, data) {
        if (this.instances[canvasId]) this.instances[canvasId].destroy();

        const ctx = document.getElementById(canvasId).getContext('2d');
        this.instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: labels,
                datasets: [{
                    data: data,
                    backgroundColor: [
                        '#6366f1', // Indigo
                        '#ec4899', // Pink
                        '#f59e0b', // Amber
                        '#10b981', // Emerald
                        '#64748b'  // Slate
                    ],
                    borderWidth: 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '70%',
                plugins: { legend: { position: 'right' } }
            }
        });
    }
};