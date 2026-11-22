const PREFIX = 'user_settings:';
const ANNOUNCEMENT_KEY = 'public_experience_preferences.announcement_bar.dismissed_edition';

/**
 * Gets all stored settings matching a key prefix (e.g., "event_list").
 * @param {string} tenant - Verified public tenant ID in N format
 * @param {string} keyPrefix - Canonical registered setting key prefix
 * @returns {Object|null} Dictionary of { settingKey: value } or null if none found
 */
export function getAll(tenant, keyPrefix) {
    const tenantPrefix = storagePrefix(tenant);
    const scanPrefix = tenantPrefix + keyPrefix + '.';
    const result = {};
    let count = 0;

    for (let i = 0; i < localStorage.length; i++) {
        const storageKey = localStorage.key(i);
        if (storageKey && storageKey.startsWith(scanPrefix)
            && !storageKey.substring(tenantPrefix.length).startsWith('public_experience_preferences.announcement_bar.')) {
            const settingKey = storageKey.substring(tenantPrefix.length);
            result[settingKey] = localStorage.getItem(storageKey);
            count++;
        }
    }

    return count > 0 ? result : null;
}

/**
 * Stores a single setting value.
 * @param {string} tenant - Verified public tenant ID in N format
 * @param {string} key - Full setting key (e.g., "event_list.browse_mode")
 * @param {string} value - Setting value
 * @returns {boolean} True only after storage succeeds
 */
export function set(tenant, key, value) {
    localStorage.setItem(storagePrefix(tenant) + key, value);
    return true;
}

/**
 * Removes a single setting override.
 * @param {string} tenant - Verified public tenant ID in N format
 * @param {string} key - Full setting key
 * @returns {boolean} True only after storage succeeds
 */
export function remove(tenant, key) {
    localStorage.removeItem(storagePrefix(tenant) + key);
    return true;
}

/**
 * Removes all settings matching a key prefix.
 * @param {string} tenant - Verified public tenant ID in N format
 * @param {string} keyPrefix - Setting key prefix (e.g., "event_list")
 * @returns {number} Number of entries removed
 */
export function clearPrefix(tenant, keyPrefix) {
    const scanPrefix = storagePrefix(tenant) + keyPrefix + '.';
    const toRemove = [];

    for (let i = 0; i < localStorage.length; i++) {
        const storageKey = localStorage.key(i);
        if (storageKey && storageKey.startsWith(scanPrefix)
            && !storageKey.substring(storagePrefix(tenant).length).startsWith('public_experience_preferences.announcement_bar.')) {
            toRemove.push(storageKey);
        }
    }

    toRemove.forEach(k => localStorage.removeItem(k));
    return toRemove.length;
}

export function getAnnouncement(tenant) {
    const stored = localStorage.getItem(storagePrefix(tenant) + ANNOUNCEMENT_KEY);
    if (stored === null) return null;
    try {
        return JSON.parse(stored);
    } catch {
        return null;
    }
}

export function setAnnouncement(tenant, marker) {
    localStorage.setItem(storagePrefix(tenant) + ANNOUNCEMENT_KEY, JSON.stringify(marker));
    return true;
}

function storagePrefix(tenant) {
    if (!/^[0-9a-f]{32}$/.test(tenant) || /^0{32}$/.test(tenant)) {
        throw new TypeError('A verified tenant identity is required.');
    }
    return PREFIX + tenant + ':';
}
