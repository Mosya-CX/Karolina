const portraitKeys = { 'discuss-requirement': 'chatPortraitDiscuss', 'formulate-plan': 'chatPortraitPlan', 'execute-task': 'chatPortraitExecute' };
/** Mode artwork listens to the existing workflow selection, independently of message and draft state. */
export function createChatPortraits(reportError) {
    const portrait = document.getElementById('stageChatPortrait');
    let assets = { }, enabled = true, generation = 0;
    const cache = new Map(), reported = new Set();
    const load = url => {
        if (!cache.has(url)) {
            const image = new Image();
            cache.set(url, new Promise((resolve, reject) => {
                image.onload = () => resolve(url);
                image.onerror = () => reject(new Error('无法加载立绘：' + url));
                image.src = url;
            }));
        }
        return cache.get(url);
    };
    const update = async () => {
        const request = ++ generation;
        const mode = document.body.dataset.workflowMode || 'discuss-requirement';
        const url = assets[portraitKeys[mode]] || assets.chatPortrait;
        const preview = document.getElementById('appearancePortraitPreview');
        portrait.hidden = !enabled || !url;
        if (preview) preview.hidden = portrait.hidden;
        document.body.dataset.chatPortrait = portrait.hidden ? 'hidden': 'visible';
        if (portrait.hidden) return;
        let selected = url;
        try {
            await load(selected);
        } catch (error) {
            if (request !== generation) return;
            if (!reported.has(url)) {
                reported.add(url);
                reportError(error.message);
            }
            selected = assets.chatPortrait;
            try {
                if (!selected || selected === url) throw error;
                await load(selected);
            } catch {
                if (request === generation) {
                    portrait.hidden = true;
                    if (preview) preview.hidden = true;
                    document.body.dataset.chatPortrait = 'hidden';
                }
                return;
            }
        }
        if (request !== generation) return;
        if (portrait.getAttribute('src') !== selected) {
            portrait.src = selected;
            // Restart only the opacity transition; pivot and zoom remain intact.
            portrait.getAnimations().forEach(animation => animation.cancel());
            if (document.documentElement.dataset.motion === 'full') portrait.animate([{ opacity: 0 }, { opacity: getComputedStyle(portrait).opacity }], { duration: 180 });
        }
        if (preview?.isConnected) preview.src = selected;
        portrait.dataset.workflowMode = mode;
    };
    document.addEventListener('karolina:workflow-mode', update);
    return {
        apply(nextAssets, preferences) {
            assets = nextAssets;
            enabled = preferences.mascotVisible;
            // Packages can update resources at the same URL; each theme transaction refreshes the loader.
            cache.clear();
            reported.clear();
            update();
        }
    };
}
