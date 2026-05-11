const BASE_URL = `http://${window.location.hostname}:5087/api`;

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

export async function updateNickname(token, nickname) {
  const response = await fetch(`${BASE_URL}/auth/nickname`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ nickname }),
  });

  if (!response.ok) {
    throw new Error("Update nickname failed");
  }

  return await response.json();
}

export async function changePassword(token, oldPassword, newPassword) {
  const response = await fetch(`${BASE_URL}/auth/password`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ oldPassword, newPassword }),
  });

  if (!response.ok) {
    const msg = await response.text();
    throw new Error(msg || "Change password failed");
  }

  return true;
}

export async function getBalances(token) {
  const response = await fetch(`${BASE_URL}/auth/balances`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!response.ok) throw new Error("Failed to get balances");
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
