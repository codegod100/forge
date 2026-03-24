window.forge = window.forge || {};

window.forge.highlightCodeBlocks = () => {
  if (window.Prism) {
    window.Prism.highlightAll();
  }
};
