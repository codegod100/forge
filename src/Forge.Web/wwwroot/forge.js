window.forge = window.forge || {};

window.forge.highlightCodeBlocks = () => {
  if (window.Prism) {
    const blocks = document.querySelectorAll('.syntax-pending code[class*="language-"]');

    if (blocks.length === 0) {
      window.Prism.highlightAll();
      return;
    }

    blocks.forEach((block) => {
      window.Prism.highlightElement(block);

      const container = block.closest('.syntax-pending');
      if (container) {
        container.classList.remove('syntax-pending');
        container.classList.add('syntax-ready');
      }
    });
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

// WebAuthn / Passkey functions
window.forge.webauthn = {
  // Convert ArrayBuffer to Base64URL string
  arrayBufferToBase64Url: (buffer) => {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    bytes.forEach(b => binary += String.fromCharCode(b));
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  },
  
  // Convert Base64URL string to ArrayBuffer
  base64UrlToArrayBuffer: (base64Url) => {
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
  },
  
  // Convert object with base64url fields to ArrayBuffer fields recursively
  decodeOptions: (options) => {
    const decoded = { ...options };
    
    if (options.challenge) {
      decoded.challenge = window.forge.webauthn.base64UrlToArrayBuffer(options.challenge);
    }
    
    if (options.user?.id) {
      decoded.user = { ...options.user };
      decoded.user.id = window.forge.webauthn.base64UrlToArrayBuffer(options.user.id);
    }
    
    if (options.allowCredentials) {
      decoded.allowCredentials = options.allowCredentials.map(c => ({
        ...c,
        id: window.forge.webauthn.base64UrlToArrayBuffer(c.id)
      }));
    }
    
    if (options.excludeCredentials) {
      decoded.excludeCredentials = options.excludeCredentials.map(c => ({
        ...c,
        id: window.forge.webauthn.base64UrlToArrayBuffer(c.id)
      }));
    }
    
    return decoded;
  },
  
  // Encode credential response for transmission
  encodeCredential: (credential) => {
    return {
      id: credential.id,
      rawId: window.forge.webauthn.arrayBufferToBase64Url(credential.rawId),
      type: credential.type,
      response: {
        clientDataJSON: window.forge.webauthn.arrayBufferToBase64Url(credential.response.clientDataJSON),
        attestationObject: window.forge.webauthn.arrayBufferToBase64Url(credential.response.attestationObject)
      },
      clientExtensionResults: credential.getClientExtensionResults()
    };
  },
  
  // Encode assertion response for transmission
  encodeAssertion: (assertion) => {
    return {
      id: assertion.id,
      rawId: window.forge.webauthn.arrayBufferToBase64Url(assertion.rawId),
      type: assertion.type,
      response: {
        clientDataJSON: window.forge.webauthn.arrayBufferToBase64Url(assertion.response.clientDataJSON),
        authenticatorData: window.forge.webauthn.arrayBufferToBase64Url(assertion.response.authenticatorData),
        signature: window.forge.webauthn.arrayBufferToBase64Url(assertion.response.signature),
        userHandle: assertion.response.userHandle 
          ? window.forge.webauthn.arrayBufferToBase64Url(assertion.response.userHandle)
          : null
      },
      clientExtensionResults: assertion.getClientExtensionResults()
    };
  },
  
  // Start passkey registration
  register: async (optionsJson) => {
    try {
      const options = window.forge.webauthn.decodeOptions(JSON.parse(optionsJson));
      const credential = await navigator.credentials.create({ publicKey: options });
      return JSON.stringify(window.forge.webauthn.encodeCredential(credential));
    } catch (error) {
      console.error('WebAuthn registration error:', error);
      throw error;
    }
  },
  
  // Start passkey authentication
  authenticate: async (optionsJson) => {
    try {
      const options = window.forge.webauthn.decodeOptions(JSON.parse(optionsJson));
      console.log('[WebAuthn] Decoded options:', options);
      const assertion = await navigator.credentials.get({ publicKey: options });
      console.log('[WebAuthn] Got assertion');
      return JSON.stringify(window.forge.webauthn.encodeAssertion(assertion));
    } catch (error) {
      console.error('[WebAuthn] Authentication error:', error.name, error.message);
      // User cancelled or no credentials found
      if (error.name === 'NotAllowedError') {
        throw new Error('No passkey found or operation was cancelled');
      }
      throw error;
    }
  },
  
  // Check if WebAuthn is available
  isAvailable: () => {
    return window.PublicKeyCredential !== undefined;
  },
  
  // Full sign-in flow
  signIn: async (redirectUrl) => {
    console.log('[WebAuthn] Starting sign-in flow...');
    try {
      // Get authentication options
      console.log('[WebAuthn] Fetching options from /auth/passkey/authenticate/start');
      const optionsResp = await fetch('/auth/passkey/authenticate/start');
      
      if (!optionsResp.ok) {
        console.error('[WebAuthn] Options request failed:', optionsResp.status, optionsResp.statusText);
        return JSON.stringify({ error: `Server error: ${optionsResp.status}` });
      }
      
      const optionsJson = await optionsResp.text();
      console.log('[WebAuthn] Options received:', optionsJson.substring(0, 200));
      
      // Call WebAuthn API
      console.log('[WebAuthn] Calling navigator.credentials.get()');
      try {
        var assertionJson = await window.forge.webauthn.authenticate(optionsJson);
        console.log('[WebAuthn] Assertion received');
      } catch (authError) {
        console.error('[WebAuthn] Authentication failed:', authError.message);
        return JSON.stringify({ error: authError.message });
      }
      
      // Send assertion to server
      console.log('[WebAuthn] Sending assertion to /auth/passkey/authenticate/complete');
      const completeResp = await fetch('/auth/passkey/authenticate/complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: assertionJson,
        credentials: 'include'
      });
      
      const result = await completeResp.json();
      console.log('[WebAuthn] Complete result:', result);
      
      if (result.success) {
        return JSON.stringify({ redirect: redirectUrl });
      } else {
        console.error('[WebAuthn] Authentication failed:', result.error);
        return JSON.stringify({ error: result.error || 'Authentication failed' });
      }
    } catch (error) {
      console.error('[WebAuthn] Sign-in error:', error);
      return JSON.stringify({ error: error.message || 'Unknown error' });
    }
  },
  
  // Full registration flow
  registerDevice: async (deviceName) => {
    try {
      // Get registration options
      const optionsResp = await fetch('/auth/passkey/register/start');
      const optionsJson = await optionsResp.text();
      
      // Call WebAuthn API
      const credentialJson = await window.forge.webauthn.register(optionsJson);
      
      // Send credential to server
      const completeResp = await fetch('/auth/passkey/register/complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...JSON.parse(credentialJson),
          deviceName: deviceName || null
        })
      });
      
      const result = await completeResp.json();
      return result;
    } catch (error) {
      console.error('Passkey registration error:', error);
      return { success: false, error: error.message };
    }
  }
};
