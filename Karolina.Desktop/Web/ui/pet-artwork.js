const poses = { idle: 'desktopPet', wave: 'desktopPetWave', hop: 'desktopPetWave', drag: 'desktopPetWave', rest: 'desktopPetSleep', work: 'desktopPetWork' };
/** Owns pose resources and asynchronous image swaps. Missing optional poses fall back to the base. */
export function createPetArtwork(image, reportError) {
    let assets = { }, serial = 0, epoch = 0;
    const loaded = new Map(), reported = new Set();
    const preload = url => {
        if (!url) return Promise.resolve(null);
        if (!loaded.has(url)) {
            const owner = epoch;
            const resource = new Image();
            loaded.set(url, new Promise(resolve => {
                resource.onload = () => resolve(url);
                resource.onerror = () => {
                    if (owner === epoch && !reported.has(url)) {
                        reported.add(url);
                        reportError('桌宠动作资源加载失败：' + url);
                    }
                    resolve(null);
                };
                resource.src = url;
            }));
        }
        return loaded.get(url);
    };
    return {
        apply(nextAssets) {
            ++ serial;
            ++ epoch;
            assets = nextAssets;
            loaded.clear();
            reported.clear();
            // Optional poses load only when used. The initial character always warms first.
            preload(assets.desktopPet || assets.mascot);
        },
        async show(action, frame = 0) {
            const request = ++ serial;
            const base = assets.desktopPet || assets.mascot;
            const key = action === 'walk' ? frame % 2 ? 'desktopPetWalkB': 'desktopPetWalkA': poses[action] || 'desktopPet';
            const selected = await preload(assets[key] || base) || await preload(base);
            if (request !== serial || !selected) return;
            if (image.getAttribute('src') !== selected) image.src = selected;
        },
        stop() {
            ++ serial;
        }
    };
}
