// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Toast Notification System
function showToast(type, title, message) {
    const container = document.getElementById('toast-container');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;

    let iconSvg = '';
    if (type === 'success') {
        iconSvg = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>`;
    } else if (type === 'error') {
        iconSvg = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>`;
    } else {
        iconSvg = `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>`;
    }

    toast.innerHTML = `
        <div class="toast-icon">${iconSvg}</div>
        <div class="toast-content">
            ${title ? `<p class="toast-title">${title}</p>` : ''}
            <p class="toast-message">${message}</p>
        </div>
    `;

    container.appendChild(toast);

    // Trigger reflow to apply starting position
    toast.offsetHeight;

    // Slide in
    toast.classList.add('show');

    // Auto dismiss after 3.5 seconds
    setTimeout(() => {
        toast.classList.remove('show');
        // Wait for transition to finish before removing from DOM
        setTimeout(() => {
            if (toast.parentNode) {
                toast.parentNode.removeChild(toast);
            }
        }, 300);
    }, 3500);
}

// Client-side Search and Filter System
function initFilters() {
    const searchInputs = document.querySelectorAll('.filter-search');
    const selectFilters = document.querySelectorAll('.filter-select');
    
    function applyFilters() {
        const cards = document.querySelectorAll('.searchable-card');
        
        // Gather current search query
        let query = '';
        searchInputs.forEach(si => { if(si.value) query = si.value.toLowerCase(); });
        
        // Gather current dropdown filter values
        const activeFilters = {};
        selectFilters.forEach(sf => {
            if (sf.value) {
                activeFilters[sf.dataset.filterKey] = sf.value.toLowerCase();
            }
        });
        
        let visibleCount = 0;
        
        cards.forEach(card => {
            let isVisible = true;
            
            // 1. Text Search matching (searches data-search attribute)
            if (query) {
                const searchText = (card.dataset.search || '').toLowerCase();
                if (!searchText.includes(query)) {
                    isVisible = false;
                }
            }
            
            // 2. Dropdown Filters matching (e.g. data-status)
            if (isVisible) {
                for (const [key, value] of Object.entries(activeFilters)) {
                    const cardValue = (card.dataset[key] || '').toLowerCase();
                    if (cardValue !== value) {
                        isVisible = false;
                        break;
                    }
                }
            }
            
            card.style.display = isVisible ? '' : 'none';
            if (isVisible) visibleCount++;
        });
    }

    searchInputs.forEach(input => input.addEventListener('input', applyFilters));
    selectFilters.forEach(select => select.addEventListener('change', applyFilters));
}

document.addEventListener('DOMContentLoaded', initFilters);
