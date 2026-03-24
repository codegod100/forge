window.forge = window.forge || {};

window.forge.highlightCodeBlocks = () => {
  if (window.Prism) {
    window.Prism.highlightAll();
  }
};

window.forge.initFileSearch = (dotNetHelper) => {
  const handleKeydown = (e) => {
    // Don't trigger if user is typing in an input or textarea
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') {
      return;
    }
    
    if (e.key === 't' || e.key === 'T') {
      e.preventDefault();
      const searchInput = document.querySelector('.file-search-input');
      if (searchInput) {
        searchInput.focus();
        dotNetHelper.invokeMethodAsync('FocusFileSearch');
      }
    }
  };
  
  document.addEventListener('keydown', handleKeydown);
  
  // Store reference so we can clean up if needed
  window.forge._fileSearchHandler = handleKeydown;
  window.forge._dotNetHelper = dotNetHelper;
};
