const backButton = document.getElementById('back');
const closeButton = document.getElementById('close');

backButton?.addEventListener('click', () => {
  if (history.length > 1) {
    history.back();
  } else {
    window.close();
  }
});

closeButton?.addEventListener('click', () => {
  window.close();
});
