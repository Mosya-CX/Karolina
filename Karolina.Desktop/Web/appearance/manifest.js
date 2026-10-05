/** Resolves partial theme packs against the complete built-in skin. Resource URLs retain their owning pack. */
export function resolveTheme(base, selected, preferences) {
    const maps = { assets: { }, icons: { }, components: { } };
    const fonts = new Map();
    const styles = [];
    for (const descriptor of base === selected ? [base]: [base, selected]) {
        const manifest = descriptor.manifest;
        for (const name of Object.keys(maps)) {
            for (const[key, path] of Object.entries(manifest[name] || { })) maps[name][key] = resourceUrl(descriptor.baseUrl, path);
        }
        // Existing packs that replace only mascot keep their character override in the new enclosure.
        if (manifest.assets?.mascot && !manifest.assets.desktopPet) maps.assets.desktopPet = resourceUrl(descriptor.baseUrl, manifest.assets.mascot);
        const petBase = manifest.assets?.desktopPet || manifest.assets?.mascot;
        if (petBase) {
            for (const key of['desktopPetWave', 'desktopPetSleep', 'desktopPetWork', 'desktopPetWalkA', 'desktopPetWalkB']) {
                if (!manifest.assets[key]) maps.assets[key] = resourceUrl(descriptor.baseUrl, petBase);
            }
        }
        if (manifest.assets?.chatPortrait) {
            for (const key of['chatPortraitDiscuss', 'chatPortraitPlan', 'chatPortraitExecute']) {
                if (!manifest.assets[key]) maps.assets[key] = resourceUrl(descriptor.baseUrl, manifest.assets.chatPortrait);
            }
        }
        for (const path of manifest.styles || []) styles.push(resourceUrl(descriptor.baseUrl, path));
        for (const font of manifest.fonts || []) fonts.set(font.family, { ... font, source: resourceUrl(descriptor.baseUrl, font.source) });
    }
    const tokens = { };
    for (const descriptor of base === selected ? [base]: [base, selected]) {
        Object.assign(tokens, descriptor.manifest.tokens, descriptor.manifest.colorModes?.[preferences.mode]);
    }
    Object.assign(tokens, preferences.tokens, { '--k-scene-opacity': String(preferences.backgroundOpacity) });
    validateTokens(tokens);
    const effectOwner = selected.manifest.effect?.fragment ? selected: base;
    const effect = {
        ... base.manifest.effect,
        ... selected.manifest.effect,
        fragment: effectOwner.manifest.effect?.fragment ? resourceUrl(effectOwner.baseUrl, effectOwner.manifest.effect.fragment): null
    };
    return { tokens, ... maps, fonts: [... fonts.values()], styles: [... new Set([... styles, ... Object.values(maps.components)])], effect };
}
export function resourceUrl(baseUrl, relative) {
    if (typeof relative !== 'string' || relative.includes('\\') || relative.includes(':') || relative.split('/').some(p => !p || p === '.' || p === '..')) throw new Error('主题资源必须使用普通相对路径');
    return baseUrl + relative.split('/').map(encodeURIComponent).join('/');
}
export function validateTokens(tokens) {
    if (!tokens || Object.keys(tokens).length > 512) throw new Error('设计变量数量无效');
    for (const[key, value] of Object.entries(tokens)) {
        if (!/^--k-[a-z0-9-]{1,100}$/.test(key) || typeof value !== 'string' || value.length > 2048 || /[{};<]/.test(value)) throw new Error('无效设计变量：' + key);
    }
}
export function effectiveMotion(mode) {
    return mode === 'system' ? matchMedia('(prefers-reduced-motion: reduce)').matches ? 'reduced': 'full': mode;
}
