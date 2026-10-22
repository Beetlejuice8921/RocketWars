// Общее для сервера рейтинга и бота: ответы, ошибки, токены, хэши.

export const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Authorization, Content-Type',
  'Access-Control-Max-Age': '86400',
};

export class HttpError extends Error {
  constructor(status, code) {
    super(code);
    this.status = status;
    this.code = code;
  }
}

export function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json', ...CORS } });
}

export const now = () => Math.floor(Date.now() / 1000);

export async function body(request) {
  try { return await request.json(); }
  catch { throw new HttpError(400, 'bad_json'); }
}

// ---------- токен ----------

export async function requireToken(request, env) {
  const t = await readToken(request, env);
  if (!t)
    throw new HttpError(401, 'no_token');
  return t;
}

export async function readToken(request, env) {
  const h = request.headers.get('Authorization') || '';
  if (!h.startsWith('Bearer '))
    return null;
  const [payload, sig] = h.slice(7).split('.');
  if (!payload || !sig)
    return null;
  const expected = b64url(await hmac(new TextEncoder().encode(env.TOKEN_SECRET), payload));
  if (!timingSafeEqual(expected, sig))
    return null;
  let data;
  try { data = JSON.parse(new TextDecoder().decode(fromB64url(payload))); } catch { return null; }
  return data && data.e > now() ? data : null;
}

export async function signToken(data, secret) {
  if (!secret)
    throw new HttpError(500, 'no_token_secret');
  const payload = b64url(new TextEncoder().encode(JSON.stringify(data)));
  return `${payload}.${b64url(await hmac(new TextEncoder().encode(secret), payload))}`;
}

// ---------- мелочи ----------

/** Имя для таблицы: без управляющих символов, до 24 знаков (как в игре). */
export function cleanName(s) {
  return String(s || '').replace(/[\u0000-\u001f\u007f<>]/g, '').trim().slice(0, 24);
}

export function randomId() {
  return hex(crypto.getRandomValues(new Uint8Array(10)));
}

export async function hmac(keyBytes, message) {
  const key = await crypto.subtle.importKey('raw', keyBytes, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return new Uint8Array(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(message)));
}

export async function sha256(text) {
  return hex(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))));
}

export function hex(bytes) {
  return [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');
}

export function b64url(bytes) {
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function fromB64url(s) {
  const bin = atob(s.replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(bin, c => c.charCodeAt(0));
}

export function timingSafeEqual(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length)
    return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
