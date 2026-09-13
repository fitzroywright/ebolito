const root = document.documentElement;
const saved = localStorage.getItem('ebolito-theme');
if (saved === 'modern' || saved === 'legacy') root.dataset.theme = saved;

document.querySelectorAll('[data-set-theme]').forEach(button => {
  button.addEventListener('click', () => {
    const theme = button.dataset.setTheme;
    root.dataset.theme = theme;
    localStorage.setItem('ebolito-theme', theme);
  });
});
