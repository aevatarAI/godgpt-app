/**
 * Features drag-and-drop reorder functionality
 * Uses SortableJS (included in LeptonX theme)
 */

function initFeatureReorder(containerSelector, onReorderCallback) {
    var container = document.querySelector(containerSelector);
    if (!container) return;

    // Check if Sortable is available (from LeptonX theme)
    if (typeof Sortable === 'undefined') {
        console.warn('SortableJS not available. Drag-and-drop disabled.');
        return;
    }

    var sortable = Sortable.create(container, {
        handle: '.drag-handle',
        animation: 150,
        ghostClass: 'bg-light',
        chosenClass: 'table-info',
        dragClass: 'shadow',
        
        onEnd: function (evt) {
            // Collect new order
            var orders = [];
            var rows = container.querySelectorAll('tr[data-id]');
            
            rows.forEach(function (row, index) {
                orders.push({
                    featureId: row.getAttribute('data-id'),
                    displayOrder: index
                });
            });

            // Call the callback with the new orders
            if (typeof onReorderCallback === 'function') {
                onReorderCallback(orders);
            }
        }
    });

    return sortable;
}
