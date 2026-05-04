/**
 * AlertHelper.js
 * Client-side companion for HR_web.Helpers.AlertHelper
 * Provides premium toast notifications using Bootstrap 5
 */

const AlertHelper = (function () {
    const containerId = 'toast-container';

    function getContainer() {
        let container = document.getElementById(containerId);
        if (!container) {
            container = document.createElement('div');
            container.id = containerId;
            container.className = 'position-fixed top-0 end-0 p-3';
            container.style.zIndex = '9999';
            document.body.appendChild(container);
        }
        return container;
    }

    function show(message, type = 'info') {
        const container = getContainer();
        
        let icon = 'info';
        let img = '../assets/img/illustrations/inform.png';
        let colorClass = 'text-bg-info';

        switch (type.toLowerCase()) {
            case 'success':
                icon = 'check_circle';
                img = '../assets/img/illustrations/success.png';
                colorClass = 'text-bg-success';
                break;
            case 'error':
            case 'danger':
                icon = 'error';
                img = '../assets/img/illustrations/error.png';
                colorClass = 'text-bg-danger';
                break;
            case 'warning':
            case 'warn':
                icon = 'warning';
                img = '../assets/img/illustrations/warning.png';
                colorClass = 'text-bg-warning';
                break;
        }

        const toastId = 'toast-' + Date.now();
        const html = `
            <div id="${toastId}" class="toast align-items-center ${colorClass} border-0 alert-toast" role="alert" aria-live="assertive" aria-atomic="true">
                <div class="d-flex align-items-center">
                    <img src="${img}" style="width:50px;height:50px;margin-right:10px;" alt="alert" />
                    <div class="toast-body flex-fill">
                        <span class="material-symbols-rounded me-2" style="vertical-align: middle;">${icon}</span>
                        <span>${escapeHtml(message)}</span>
                    </div>
                    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
                </div>
            </div>`;

        container.insertAdjacentHTML('beforeend', html);
        const toastEl = document.getElementById(toastId);
        const toast = new bootstrap.Toast(toastEl, { delay: 5000 });
        toast.show();

        // Cleanup after hidden
        toastEl.addEventListener('hidden.bs.toast', function () {
            toastEl.remove();
        });
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    return {
        success: (msg) => show(msg, 'success'),
        error: (msg) => show(msg, 'error'),
        info: (msg) => show(msg, 'info'),
        warn: (msg) => show(msg, 'warning'),
        show: show
    };
})();
