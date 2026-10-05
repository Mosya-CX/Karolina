import { session } from '../core/api.js';
import { resolveTheme, effectiveMotion } from './manifest.js';
import { createChatPortraits } from './chat-portraits.js';
/** Transactional appearance application. Failed resources leave the previously displayed theme intact. */
export function createThemeRenderer({ components, effects, mascot, reportError }) {
    const portraits = createChatPortraits(reportError);
    let generation = 0;
    let activeLinks = [];
    let activeStyle;
    let activePreferences;
    const loadStylesheet = url => new Promise((resolve, reject) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = url;
        link.media = 'not all';
        const timer = setTimeout(() => {
            link.remove();
            reject(new Error('主题样式加载超时'));
        }, 8000);
        link.onload = () => {
            clearTimeout(timer);
            resolve(link);
        };
        link.onerror = () => {
            clearTimeout(timer);
            link.remove();
            reject(new Error('无法加载主题样式：' + url));
        };
        document.head.append(link);
    });
    const apply = async (base, selected, preferences) => {
        const request = ++ generation;
        const theme = resolveTheme(base, selected, preferences);
        const loaded = await Promise.allSettled(theme.styles.map(loadStylesheet));
        const links = loaded.filter(r => r.status === 'fulfilled').map(r => r.value);
        if (request !== generation) {
            links.forEach(link => link.remove());
            return false;
        }
        const failure = loaded.find(r => r.status === 'rejected');
        if (failure) {
            links.forEach(link => link.remove());
            throw failure.reason;
        }
        const style = document.createElement('style');
        style.nonce = session;
        style.dataset.kTheme = 'tokens';
        const fontRules = theme.fonts.map(font => `@font-face{font-family:${JSON.stringify(font.family)};src:url(${JSON.stringify(font.source)});font-weight:${font.weight||'400'};font-style:${font.style||'normal'};font-display:swap;}`).join('\n');
        style.textContent = `${fontRules}\n:root{${Object.entries(theme.tokens).map(([key,value])=>key+':'+value).join(';')}}`;
        document.head.append(style);
        links.forEach(link => link.media = 'all');
        activeLinks.forEach(link => link.remove());
        activeStyle?.remove();
        activeLinks = links;
        activeStyle = style;
        activePreferences = structuredClone(preferences);
        document.documentElement.dataset.colorMode = preferences.mode;
        document.documentElement.dataset.motion = effectiveMotion(preferences.motion);
        document.documentElement.dataset.themeId = selected.manifest.id;
        document.body.classList.toggle('dark', preferences.mode === 'dark');
        components.applyTheme(theme.icons, theme.assets);
        const background = document.getElementById('stageBackground');
        if (theme.assets.background) {
            background.src = theme.assets.background;
            background.hidden = false;
        } else background.hidden = true;
        for (const name of['sky', 'city', 'landmark', 'foreground']) {
            const image = document.getElementById('stage' + name[0].toUpperCase() + name.slice(1));
            image.hidden = !theme.assets[name];
            if (theme.assets[name]) image.src = theme.assets[name];
        }
        portraits.apply(theme.assets, preferences);
        mascot.apply(theme.assets, preferences);
        effects.configure({ ... theme.effect, mode: preferences.effects, motion: effectiveMotion(preferences.motion) });
        document.dispatchEvent(new CustomEvent('karolina:appearance', { detail: { preferences: activePreferences, themeId: selected.manifest.id } }));
        return true;
    };
    const motionQuery = matchMedia('(prefers-reduced-motion: reduce)');
    motionQuery.addEventListener('change', () => {
        if (activePreferences?.motion === 'system') {
            const motion = effectiveMotion('system');
            document.documentElement.dataset.motion = motion;
            effects.setMotion(motion);
        }
    });
    return { apply };
}
