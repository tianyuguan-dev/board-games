const BASE_URL = import.meta.env.VITE_BACKEND_URL
  ? `${import.meta.env.VITE_BACKEND_URL}/api`
  : (import.meta.env.PROD ? '/api' : `http://${window.location.hostname}:5087/api`);

let refreshPromise = null;

function forceLogout() {
  localStorage.removeItem("token");
  localStorage.removeItem("refreshToken");
  localStorage.removeItem("nickname");
  sessionStorage.removeItem("selectedGame");
  sessionStorage.removeItem("roomId");
  window.location.reload();
}

export async function refreshAccessToken() {
  if (refreshPromise) return refreshPromise;

  refreshPromise = (async () => {
    const accessToken = localStorage.getItem("token");
    const refreshToken = localStorage.getItem("refreshToken");
    if (!refreshToken) {
      forceLogout();
      return null;
    }

    try {
      const response = await fetch(`${BASE_URL}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accessToken: accessToken || "", refreshToken }),
      });

      if (!response.ok) {
        forceLogout();
        return null;
      }

      const data = await response.json();
      localStorage.setItem("token", data.token);
      localStorage.setItem("refreshToken", data.refreshToken);
      return data.token;
    } catch {
      forceLogout();
      return null;
    }
  })();

  try {
    return await refreshPromise;
  } finally {
    refreshPromise = null;
  }
}

export function isGuestToken() {
  const token = localStorage.getItem("token");
  if (!token) return false;
  try {
    const payload = JSON.parse(atob(token.split('.')[1]));
    return payload.isGuest === "true" || payload.isGuest === true;
  } catch {
    return false;
  }
}

export async function getValidToken() {
  const token = localStorage.getItem("token");
  if (!token) return null;

  try {
    const payload = JSON.parse(atob(token.split('.')[1]));
    if (payload.exp * 1000 < Date.now()) {
      return await refreshAccessToken();
    }
  } catch {
    return token;
  }

  return token;
}

async function authFetch(url, options = {}) {
  let token = await getValidToken();
  if (!token) return null;

  const response = await fetch(url, {
    ...options,
    headers: {
      ...options.headers,
      Authorization: `Bearer ${token}`,
    },
  });

  if (response.status === 401) {
    token = await refreshAccessToken();
    if (!token) return null;

    return fetch(url, {
      ...options,
      headers: {
        ...options.headers,
        Authorization: `Bearer ${token}`,
      },
    });
  }

  return response;
}

export async function login(username, password) {
  const response = await fetch(`${BASE_URL}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });

  if (!response.ok) {
    throw new Error("Login failed");
  }

  return await response.json();
}

export async function loginAsGuest() {
  const response = await fetch(`${BASE_URL}/auth/guest`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
  if (!response.ok) throw new Error("Guest login failed");
  return await response.json();
}

export async function register(username, password) {
  const response = await fetch(`${BASE_URL}/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });

  if (!response.ok) {
    const msg = await response.text();
    throw new Error(msg || "Register failed");
  }

  return true;
}

export async function updateNickname(token, nickname) {
  const response = await authFetch(`${BASE_URL}/auth/nickname`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ nickname }),
  });

  if (!response || !response.ok) {
    throw new Error("Update nickname failed");
  }

  return await response.json();
}

export async function changePassword(token, oldPassword, newPassword) {
  const response = await authFetch(`${BASE_URL}/auth/password`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ oldPassword, newPassword }),
  });

  if (!response || !response.ok) {
    const msg = response ? await response.text() : "Change password failed";
    throw new Error(msg || "Change password failed");
  }

  return true;
}

export async function getBalances(token) {
  const response = await authFetch(`${BASE_URL}/auth/balances`);

  if (!response || !response.ok) throw new Error("Failed to get balances");
  return await response.json();
}

export async function getMyAvalonGames(limit = 20, offset = 0) {
  const response = await authFetch(`${BASE_URL}/avalon/games/recent?limit=${limit}&offset=${offset}`);
  if (!response || !response.ok) throw new Error("Failed to load game history");
  return await response.json();
}

export async function getAvalonGameDetail(id) {
  const response = await authFetch(`${BASE_URL}/avalon/games/${id}`);
  if (!response) throw new Error("Not authenticated");
  if (response.status === 404) return null;
  if (!response.ok) throw new Error("Failed to load game detail");
  return await response.json();
}

export async function logout() {
  const refreshToken = localStorage.getItem("refreshToken");
  if (refreshToken) {
    try {
      await fetch(`${BASE_URL}/auth/logout`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accessToken: "", refreshToken }),
      });
    } catch {}
  }
  forceLogout();
}
