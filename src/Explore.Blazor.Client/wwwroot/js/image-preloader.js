export function preloadImages(urls) {
    if (!urls || urls.length === 0) return Promise.resolve();

    const promises = urls.map(url => new Promise((resolve) => {
        const img = new Image();
        img.onload = resolve;
        img.onerror = resolve;
        img.src = url;
    }));

    return Promise.all(promises);
}
