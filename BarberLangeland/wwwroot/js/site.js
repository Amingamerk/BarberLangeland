// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.


// Collapse the mobile menu after picking an in-page link such as "Kontakt".
document.querySelectorAll('[data-mobile-menu-close]').forEach(function (link) {
    link.addEventListener('click', function () {
        var menu = document.getElementById('mobileMenu');
        if (menu && window.bootstrap) {
            window.bootstrap.Collapse.getOrCreateInstance(menu).hide();
        }
    });
});
