const PREFIX = 'user_settings:';

/**
 * Gets all stored settings matching a key prefix (e.g., "event_list").
 * @param {string} keyPrefix - Setting key prefix (underscore format, e.g., "event_list")
 * @returns {Object|null} Dictionary of { settingKey: value } or null if none found
 */
export function getAll(keyPrefix) {
    const scanPrefix = PREFIX + keyPrefix + '.';
    const result = {};
    let count = 0;

    for (let i = 0; i < localStorage.length; i++) {
        const storageKey = localStorage.key(i);
        if (storageKey && storageKey.startsWith(scanPrefix)) {
            const settingKey = storageKey.substring(PREFIX.length);
            result[settingKey] = localStorage.getItem(storageKey);
            count++;
        }
    }

    return count > 0 ? result : null;
}

/**
 * Stores a single setting value.
 * @param {string} key - Full setting key (e.g., "event_list.browse_mode")
 * @param {string} value - Setting value
 */
export function set(key, value) {
    localStorage.setItem(PREFIX + key, value);
}

/**
 * Removes a single setting override.
 * @param {string} key - Full setting key
 */
export function remove(key) {
    localStorage.removeItem(PREFIX + key);
}

/**
 * Removes all settings matching a key prefix.
 * @param {string} keyPrefix - Setting key prefix (e.g., "event_list")
 * @returns {number} Number of entries removed
 */
export function clearPrefix(keyPrefix) {
    const scanPrefix = PREFIX + keyPrefix + '.';
    const toRemove = [];

    for (let i = 0; i < localStorage.length; i++) {
        const storageKey = localStorage.key(i);
        if (storageKey && storageKey.startsWith(scanPrefix)) {
            toRemove.push(storageKey);
        }
    }

    toRemove.forEach(k => localStorage.removeItem(k));
    return toRemove.length;
}
