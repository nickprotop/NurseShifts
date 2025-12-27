/**
 * Schedule Manager Module
 * Handles all schedule page interactions including:
 * - Adding/removing nurse assignments
 * - Toggling manual/auto assignments
 * - Modal management
 * - AJAX operations (with future enhancement for no-reload updates)
 */
const ScheduleManager = (function() {
    'use strict';

    // Configuration - populated from data attributes
    let config = {
        strings: {}
    };

    // State
    let currentCell = null;
    let addNurseModal = null;

    // Local storage keys
    const STORAGE_KEYS = {
        WEEKS_PREFERENCE: 'schedule_weeks',
        CLINIC_PREFERENCE: 'schedule_clinic'
    };

    /**
     * Initialize the schedule manager
     */
    function init() {
        // Load localized strings from data attributes
        const scheduleContainer = document.getElementById('schedule-container');
        if (scheduleContainer) {
            config.strings = JSON.parse(scheduleContainer.dataset.strings || '{}');
        }

        // Initialize Bootstrap modal
        const modalEl = document.getElementById('addNurseModal');
        if (modalEl) {
            addNurseModal = new bootstrap.Modal(modalEl);
        }

        // Attach event handlers
        attachAddNurseHandlers();
        attachToggleManualHandlers();
        attachRemoveAssignmentHandlers();
        attachWorkloadPanelHandler();

        // Save current preferences to localStorage
        savePreferences();

        // Apply preferences on first load if no explicit params
        applyStoredPreferencesOnLoad();
    }

    /**
     * Save current view preferences to localStorage
     */
    function savePreferences() {
        const clinicSelect = document.querySelector('select[name="clinicId"]');

        if (clinicSelect && clinicSelect.value) {
            localStorage.setItem(STORAGE_KEYS.CLINIC_PREFERENCE, clinicSelect.value);
        }

        // Determine current weeks from URL
        const urlParams = new URLSearchParams(window.location.search);
        const weeks = urlParams.get('weeks');
        if (weeks) {
            localStorage.setItem(STORAGE_KEYS.WEEKS_PREFERENCE, weeks);
        }
    }

    /**
     * Apply stored preferences on first load (when no URL params)
     */
    function applyStoredPreferencesOnLoad() {
        const urlParams = new URLSearchParams(window.location.search);

        // Only redirect if this looks like a fresh load with no preferences in URL
        // and we have stored preferences
        if (!urlParams.has('clinicId') && !urlParams.has('weeks') && !urlParams.has('startDate')) {
            const storedClinic = localStorage.getItem(STORAGE_KEYS.CLINIC_PREFERENCE);
            const storedWeeks = localStorage.getItem(STORAGE_KEYS.WEEKS_PREFERENCE);

            // Only redirect if we have preferences and they differ from defaults
            if (storedClinic || storedWeeks) {
                const clinicSelect = document.querySelector('select[name="clinicId"]');
                const currentClinic = clinicSelect?.value;

                // Build redirect URL with stored preferences if different from current
                let shouldRedirect = false;
                let newUrl = new URL(window.location.href);

                if (storedClinic && storedClinic !== currentClinic) {
                    newUrl.searchParams.set('clinicId', storedClinic);
                    shouldRedirect = true;
                }

                if (storedWeeks && storedWeeks !== '1') {  // 1 week is default
                    newUrl.searchParams.set('weeks', storedWeeks);
                    shouldRedirect = true;
                }

                if (shouldRedirect) {
                    window.location.href = newUrl.toString();
                }
            }
        }
    }

    /**
     * Attach click handlers to all "Add Nurse" buttons
     */
    function attachAddNurseHandlers() {
        document.querySelectorAll('.add-nurse-btn').forEach(btn => {
            btn.addEventListener('click', handleAddNurseClick);
        });
    }

    /**
     * Handle click on "Add Nurse" button
     */
    function handleAddNurseClick(e) {
        currentCell = this.closest('td');
        const clinicId = currentCell.dataset.clinic;
        const date = currentCell.dataset.date;
        const shiftType = currentCell.dataset.shift;

        // Show loading state
        showLoadingInModal();
        addNurseModal.show();

        // Fetch available nurses
        fetchAvailableNurses(clinicId, date, shiftType);
    }

    /**
     * Show loading spinner in modal
     */
    function showLoadingInModal() {
        const container = document.getElementById('availableNursesList');
        container.innerHTML = `
            <div class="text-center py-3">
                <div class="spinner-border spinner-border-sm" role="status"></div>
                ${config.strings.loading || 'Loading...'}
            </div>`;
    }

    /**
     * Fetch available nurses for a shift
     */
    function fetchAvailableNurses(clinicId, date, shiftType) {
        fetch(`/Schedule/GetAvailableNurses?clinicId=${clinicId}&date=${date}&shiftType=${shiftType}`)
            .then(r => r.json())
            .then(nurses => renderNursesList(nurses, clinicId, date, shiftType))
            .catch(err => {
                console.error('Error fetching nurses:', err);
                document.getElementById('availableNursesList').innerHTML =
                    `<p class="text-danger text-center">${config.strings.updateFailed || 'Failed to load nurses'}</p>`;
            });
    }

    /**
     * Render the list of available nurses in the modal
     */
    function renderNursesList(nurses, clinicId, date, shiftType) {
        const container = document.getElementById('availableNursesList');

        if (nurses.length === 0) {
            container.innerHTML = `<p class="text-muted text-center">${config.strings.noNursesAvailable || 'No nurses available'}</p>`;
            return;
        }

        let html = `
            <div class="form-check mb-3 border-bottom pb-2">
                <input type="checkbox" class="form-check-input" id="markAsManual">
                <label class="form-check-label" for="markAsManual">
                    <i class="bi bi-lock-fill text-primary me-1"></i>${config.strings.markAsManual || 'Mark as Manual'}
                </label>
                <div class="form-text small">${config.strings.markAsManualHelp || 'Manual assignments are preserved during auto-generation'}</div>
            </div>
            <div class="list-group">`;

        nurses.forEach(nurse => {
            const available = nurse.isAvailable;
            const borrowed = nurse.isBorrowed;

            html += `
                <div class="list-group-item ${!available ? 'list-group-item-secondary' : ''}">
                    <div class="d-flex justify-content-between align-items-center">
                        <div>
                            <strong>${escapeHtml(nurse.name)}</strong>
                            ${borrowed ? `<span class="badge bg-info ms-1">${config.strings.borrowed || 'Borrowed'}</span>` : ''}
                            ${nurse.canHandleResponsibility ? `<i class="bi bi-star-fill text-warning ms-1" title="${config.strings.canHandleResponsibility || 'Can handle responsibility'}"></i>` : ''}
                            ${!available ? `<br><small class="text-muted">${nurse.reasons.join(', ')}</small>` : ''}
                        </div>
                        <div>
                            ${available ? `
                                <button type="button" class="btn btn-sm btn-outline-primary assign-nurse"
                                        data-nurse-id="${nurse.nurseId}" data-responsible="false">
                                    ${config.strings.assign || 'Assign'}
                                </button>
                                ${nurse.canHandleResponsibility ? `
                                    <button type="button" class="btn btn-sm btn-warning assign-nurse"
                                            data-nurse-id="${nurse.nurseId}" data-responsible="true"
                                            title="${config.strings.assignAsResponsible || 'Assign as responsible'}">
                                        <i class="bi bi-star-fill"></i>
                                    </button>
                                ` : ''}
                            ` : ''}
                        </div>
                    </div>
                </div>`;
        });

        html += '</div>';
        container.innerHTML = html;

        // Attach handlers to assign buttons
        container.querySelectorAll('.assign-nurse').forEach(btn => {
            btn.addEventListener('click', function() {
                const nurseId = this.dataset.nurseId;
                const isResponsible = this.dataset.responsible === 'true';
                const isManual = document.getElementById('markAsManual')?.checked || false;
                assignNurse(clinicId, date, shiftType, nurseId, isResponsible, isManual);
            });
        });
    }

    /**
     * Assign a nurse to a shift
     */
    function assignNurse(clinicId, date, shiftType, nurseId, isResponsible, isManual) {
        const body = `clinicId=${clinicId}&date=${date}&shiftType=${shiftType}&nurseId=${nurseId}&isResponsible=${isResponsible}&isManual=${isManual}`;

        // Show loading state on the cell
        if (currentCell) {
            currentCell.classList.add('cell-loading');
        }

        fetch('/Schedule/AssignNurse', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body
        }).then(r => r.json())
        .then(result => {
            if (result.success) {
                // Update the cell with new HTML
                updateCellContent(currentCell, result);
                addNurseModal.hide();
                showToast(config.strings.updateSuccess || 'Updated successfully', 'success');
            } else {
                showToast(result.error || config.strings.couldNotAssignNurse || 'Could not assign nurse', 'danger');
            }
        }).catch(err => {
            console.error('Error assigning nurse:', err);
            showToast(config.strings.updateFailed || 'Update failed', 'danger');
        }).finally(() => {
            if (currentCell) {
                currentCell.classList.remove('cell-loading');
            }
        });
    }

    /**
     * Attach handlers for toggle manual buttons
     */
    function attachToggleManualHandlers() {
        document.querySelectorAll('.toggle-manual').forEach(btn => {
            btn.addEventListener('click', handleToggleManual);
        });
    }

    /**
     * Handle toggle manual assignment
     */
    function handleToggleManual(e) {
        const item = this.closest('.assignment-item');
        const cell = this.closest('td');
        const assignmentId = item.dataset.assignmentId;

        cell.classList.add('cell-loading');

        fetch('/Schedule/ToggleManual', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `assignmentId=${assignmentId}`
        }).then(r => r.json())
        .then(result => {
            if (result.success) {
                updateCellContent(cell, result);
                showToast(config.strings.updateSuccess || 'Updated successfully', 'success');
            } else {
                showToast(config.strings.updateFailed || 'Update failed', 'danger');
            }
        }).catch(err => {
            console.error('Error toggling manual:', err);
            showToast(config.strings.updateFailed || 'Update failed', 'danger');
        }).finally(() => {
            cell.classList.remove('cell-loading');
        });
    }

    /**
     * Attach handlers for remove assignment buttons
     */
    function attachRemoveAssignmentHandlers() {
        document.querySelectorAll('.remove-assignment').forEach(btn => {
            btn.addEventListener('click', handleRemoveAssignment);
        });
    }

    /**
     * Handle remove assignment
     */
    function handleRemoveAssignment(e) {
        const item = this.closest('.assignment-item');
        const cell = this.closest('td');
        const assignmentId = item.dataset.assignmentId;
        const isManual = item.dataset.isManual === 'true';

        if (isManual) {
            const confirmMsg = config.strings.confirmRemoveManual || 'This is a manual assignment. Are you sure you want to remove it?';
            if (!confirm(confirmMsg)) {
                return;
            }
        }

        const force = isManual ? 'true' : 'false';
        cell.classList.add('cell-loading');

        fetch('/Schedule/RemoveAssignment', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: `assignmentId=${assignmentId}&force=${force}`
        }).then(r => r.json())
        .then(result => {
            if (result.success) {
                updateCellContent(cell, result);
                showToast(config.strings.updateSuccess || 'Updated successfully', 'success');
            } else {
                showToast(result.error || config.strings.updateFailed || 'Update failed', 'danger');
            }
        }).catch(err => {
            console.error('Error removing assignment:', err);
            showToast(config.strings.updateFailed || 'Update failed', 'danger');
        }).finally(() => {
            cell.classList.remove('cell-loading');
        });
    }

    /**
     * Attach handler for workload panel collapse
     */
    function attachWorkloadPanelHandler() {
        const workloadContent = document.getElementById('workloadContent');
        const workloadToggle = document.getElementById('workloadToggle');

        if (!workloadContent || !workloadToggle) return;

        let workloadLoaded = false;

        // Toggle button text and icon
        workloadContent.addEventListener('show.bs.collapse', function () {
            workloadToggle.querySelector('.show-text').style.display = 'none';
            workloadToggle.querySelector('.hide-text').style.display = 'inline';
            workloadToggle.querySelector('i').classList.replace('bi-chevron-down', 'bi-chevron-up');

            if (!workloadLoaded) {
                loadWorkloadData();
                workloadLoaded = true;
            }
        });

        workloadContent.addEventListener('hide.bs.collapse', function () {
            workloadToggle.querySelector('.show-text').style.display = 'inline';
            workloadToggle.querySelector('.hide-text').style.display = 'none';
            workloadToggle.querySelector('i').classList.replace('bi-chevron-up', 'bi-chevron-down');
        });
    }

    /**
     * Load workload summary data
     */
    function loadWorkloadData() {
        const workloadContent = document.getElementById('workloadContent');
        const container = document.getElementById('workloadPanelContent');

        if (!workloadContent || !container) return;

        const clinicId = workloadContent.dataset.clinic;
        const startDate = workloadContent.dataset.startDate;
        const weeks = workloadContent.dataset.weeks;

        fetch(`/Schedule/GetWorkloadSummary?clinicId=${clinicId}&startDate=${startDate}&weeks=${weeks}`)
            .then(r => r.json())
            .then(data => {
                container.innerHTML = renderWorkloadPanel(data);
            })
            .catch(err => {
                console.error('Error loading workload:', err);
                container.innerHTML = `<p class="text-danger text-center">${config.strings.updateFailed || 'Failed to load data'}</p>`;
            });
    }

    /**
     * Render workload panel HTML from data
     */
    function renderWorkloadPanel(data) {
        if (!data.workloads || data.workloads.length === 0) {
            return '<p class="text-muted text-center mb-0">No data available</p>';
        }

        let html = '<div class="workload-panel">';

        data.workloads.forEach(w => {
            const percentage = Math.min(100, w.percentageOfContract);
            const statusClass = w.status;
            const overtimeClass = w.overtimeHours > 0 ? 'text-warning fw-bold' : '';

            html += `
                <div class="workload-item">
                    <div class="workload-nurse-name" title="${escapeHtml(w.nurseName)}">
                        ${escapeHtml(w.nurseName)}
                    </div>
                    <div class="workload-bar-container">
                        <div class="workload-bar ${statusClass}"
                             style="width: ${percentage}%"
                             title="${w.totalHours}h / ${w.contractedHours}h (${w.percentageOfContract}%)">
                        </div>
                    </div>
                    <div class="workload-hours">
                        <span class="${overtimeClass}">${w.totalHours}h</span>
                        <small class="text-muted">/ ${w.contractedHours}h</small>
                    </div>
                </div>`;
        });

        html += '</div>';
        return html;
    }

    /**
     * Update cell content with new HTML from server response
     */
    function updateCellContent(cell, result) {
        if (!cell || !result.cellHtml) return;

        // Get the shift-cell div inside the td
        const shiftCell = cell.querySelector('.shift-cell');
        if (shiftCell) {
            // Replace the entire shift-cell content
            shiftCell.outerHTML = result.cellHtml;
        } else {
            // If no shift-cell, insert the new HTML
            cell.innerHTML = result.cellHtml;
        }

        // Update validation state on the cell
        cell.classList.remove('table-danger', 'table-warning');
        if (result.validation) {
            if (result.validation.errors && result.validation.errors.length > 0) {
                cell.classList.add('table-danger');
            } else if (result.validation.warnings && result.validation.warnings.length > 0) {
                cell.classList.add('table-warning');
            }
        }

        // Re-attach event handlers to new elements
        cell.querySelectorAll('.add-nurse-btn').forEach(btn => {
            btn.addEventListener('click', handleAddNurseClick);
        });
        cell.querySelectorAll('.toggle-manual').forEach(btn => {
            btn.addEventListener('click', handleToggleManual);
        });
        cell.querySelectorAll('.remove-assignment').forEach(btn => {
            btn.addEventListener('click', handleRemoveAssignment);
        });
    }

    /**
     * Show a toast notification
     */
    function showToast(message, type = 'info') {
        // Create toast container if it doesn't exist
        let toastContainer = document.querySelector('.toast-container');
        if (!toastContainer) {
            toastContainer = document.createElement('div');
            toastContainer.className = 'toast-container position-fixed bottom-0 end-0 p-3';
            toastContainer.style.zIndex = '1050';
            document.body.appendChild(toastContainer);
        }

        // Create toast element
        const toastId = 'toast-' + Date.now();
        const bgClass = type === 'success' ? 'bg-success' : type === 'danger' ? 'bg-danger' : 'bg-info';
        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = `toast ${bgClass} text-white`;
        toast.setAttribute('role', 'alert');
        toast.innerHTML = `
            <div class="toast-body d-flex justify-content-between align-items-center">
                ${escapeHtml(message)}
                <button type="button" class="btn-close btn-close-white ms-2" data-bs-dismiss="toast"></button>
            </div>`;

        toastContainer.appendChild(toast);

        // Show toast using Bootstrap
        const bsToast = new bootstrap.Toast(toast, { delay: 3000 });
        bsToast.show();

        // Remove from DOM after hidden
        toast.addEventListener('hidden.bs.toast', () => toast.remove());
    }

    /**
     * Escape HTML to prevent XSS
     */
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Public API
    return {
        init: init
    };
})();

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', ScheduleManager.init);
