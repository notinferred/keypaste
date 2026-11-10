const header = document.querySelector('.top');
const markScrolled = () => header.classList.toggle('scrolled', window.scrollY > 8);
window.addEventListener('scroll', markScrolled, { passive: true });
markScrolled();

const STARS_KEY = 'keypaste-github-stars';
const showStars = (count) => {
  for (const slot of document.querySelectorAll('[data-github-stars]')) {
    slot.textContent = new Intl.NumberFormat('en', { notation: 'compact' }).format(count);
    slot.hidden = false;
  }
};

// The count stays hidden when GitHub does not answer, so the page never shows a made-up number.
const loadStars = async () => {
  const cached = Number(sessionStorage.getItem(STARS_KEY));
  if (cached) return showStars(cached);
  const response = await fetch('https://api.github.com/repos/notinferred/keypaste');
  if (!response.ok) return;
  const { stargazers_count: count } = await response.json();
  if (!Number.isInteger(count)) return;
  sessionStorage.setItem(STARS_KEY, String(count));
  showStars(count);
};
loadStars().catch(() => {});
