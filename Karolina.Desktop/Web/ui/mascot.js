import { createDesktopPet } from './desktop-pet.js';
/** A chibi pet and a basic-geometric phone face express real workspace state. */
export function createMascot(ui) {
    const pet = createDesktopPet(ui);
    let lastEmotion = '', lastState = { }, overrideUntil = 0, override = null, assets = { };
    const shapes = {
        idle: { eyes: [[14, 20, 4], [34, 20, 4]], brows: ['M9 11L18 11', 'M30 11L39 11'], mouth: 'M17 36Q24 41 31 36' },
        working: { eyes: [[14, 21, 3], [34, 18, 5]], brows: ['M8 12L19 14', 'M29 10L40 8'], mouth: 'M21 37L28 37' },
        happy: { eyes: [[14, 19, 5], [34, 19, 5]], brows: ['M8 9L19 7', 'M29 7L40 9'], mouth: 'M14 33Q24 50 34 33' },
        error: { eyes: [[14, 23, 3], [34, 23, 3]], brows: ['M8 11L19 16', 'M29 16L40 11'], mouth: 'M17 40Q24 32 31 40' },
        offline: { eyes: [[14, 23, 2], [34, 23, 2]], brows: ['M8 15L19 15', 'M29 15L40 15'], mouth: 'M20 37L28 37' }
    };
    const render = emotion => {
        if (emotion === lastEmotion) return;
        lastEmotion = emotion;
        const shape = shapes[emotion];
        document.querySelectorAll('[data-agent-phone]').forEach(phone => {
            phone.dataset.emotion = emotion;
            phone.querySelectorAll('.k-agent-eye').forEach((circle, i) => {
                const[cx, cy, r] = shape.eyes[i];
                circle.setAttribute('cx', cx);
                circle.setAttribute('cy', cy);
                circle.setAttribute('r', r);
            });
            phone.querySelectorAll('.k-agent-brow').forEach((line, i) => line.setAttribute('d', shape.brows[i]));
            phone.querySelector('.k-agent-mouth')?.setAttribute('d', shape.mouth);
        });
        const key = 'agent' + emotion[0].toUpperCase() + emotion.slice(1);
        const art = ui.$ ('agentFaceArtwork');
        art.hidden = !assets[key] && !assets.agentFace;
        if (!art.hidden) art.src = assets[key] || assets.agentFace;
        ui.$ ('agentFace').hidden = !art.hidden;
        const titles = { idle: '准备好了', working: '正在认真处理', happy: '完成啦', error: '遇到问题了', offline: '等你连接' };
        ui.$ ('mascotStatus').textContent = titles[emotion];
        ui.$ ('agentPhone').setAttribute('aria-label', titles[emotion]);
        pet.setEmotion(emotion);
    };
    const update = state => {
        lastState = state;
        render(state.busy || state.toolBusy ? 'working': Date.now() < overrideUntil ? override: state.codex?.connected ? 'idle': 'offline');
    };
    document.addEventListener('karolina:turn-state', event => {
        override = event.detail === 'completed' ? 'happy': event.detail === 'failed' ? 'error': 'idle';
        overrideUntil = Date.now() + 6000;
        update(lastState);
    });
    return {
        update,
        apply(nextAssets, preferences) {
            assets = nextAssets;
            ui.$ ('mascot').hidden = !preferences.mascotVisible;
            pet.apply(assets, preferences);
            lastEmotion = '';
            update(lastState);
        }
    };
}
