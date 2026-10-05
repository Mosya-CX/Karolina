import { createPetArtwork } from './pet-artwork.js';
/** Activity owns only local visual state. Real task emotion is supplied by mascot.js. */
export function createDesktopPet(ui) {
    const stage = ui.$ ('petStage'), actor = ui.$ ('mascotPet'), host = ui.$ ('mascot');
    const ball = ui.$ ('petBall'), bubble = ui.$ ('petBubble');
    const artwork = createPetArtwork(ui.$ ('mascotPortrait'), ui.toast);
    const events = new AbortController(), options = { signal: events.signal };
    let x = 0, y = 0, initialized = false, enabled = true, emotion = 'idle', mode = 'roam', action = 'idle';
    let roamTimer, actionTimer, frameTimer, bubbleTimer, drag = null, playing = false, destroyed = false, suppressClick = false, lastFollow = 0;
    const visible = () => !destroyed && enabled && !document.hidden && stage.clientWidth > 0 && !host.hidden;
    const animated = () => visible() && mode !== 'paused' && document.documentElement.dataset.motion === 'full';
    const busy = () => emotion === 'working';
    const bounds = () => ({ x: Math.max(0, stage.clientWidth - actor.offsetWidth), y: Math.max(0, stage.clientHeight - actor.offsetHeight - 8) });
    const clamp = (value, max) => Math.max(0, Math.min(max, value));
    const position = (nextX, nextY) => {
        const max = bounds(), targetX = clamp(nextX, max.x);
        if (Math.abs(targetX - x) > 1) actor.dataset.facing = targetX < x ? 'left': 'right';
        x = targetX;
        y = clamp(nextY, max.y);
        actor.style.setProperty('--k-pet-x', x + 'px');
        actor.style.setProperty('--k-pet-y', y + 'px');
    };
    const setAction = next => {
        clearTimeout(frameTimer);
        action = next;
        actor.dataset.action = next;
        artwork.show(next);
        if (next === 'walk' && animated()) {
            let frame = 0;
            const step = () => {
                if (action !== 'walk' || !animated()) return;
                artwork.show('walk', ++ frame);
                frameTimer = setTimeout(step, 240);
            };
            frameTimer = setTimeout(step, 240);
        }
    };
    const freeze = () => {
        if (action !== 'walk' || !visible()) return;
        const current = actor.getBoundingClientRect(), region = stage.getBoundingClientRect();
        const facing = actor.dataset.facing;
        setAction('idle');
        position(current.left - region.left - stage.clientLeft, current.top - region.top - stage.clientTop);
        actor.dataset.facing = facing;
    };
    const stop = () => {
        freeze();
        clearTimeout(roamTimer);
        clearTimeout(actionTimer);
        clearTimeout(frameTimer);
        playing = false;
        ball.hidden = true;
        actor.style.removeProperty('--k-pet-drag-tilt');
    };
    const announce = text => {
        clearTimeout(bubbleTimer);
        bubble.textContent = text;
        bubble.hidden = false;
        bubbleTimer = setTimeout(() => bubble.hidden = true, 1600);
    };
    const controls = () => {
        host.dataset.petMotion = animated() ? 'running': 'paused';
        ui.$ ('petPlay').disabled = busy() || !animated();
        ui.$ ('petRest').disabled = busy();
        ui.$ ('petFollow').disabled = busy() || !animated();
        for (const[id, selected] of[['petRest', mode === 'rest'], ['petFollow', mode === 'follow'], ['petPause', mode === 'paused']]) {
            ui.$ (id).setAttribute('aria-pressed', String(selected));
            ui.$ (id).classList.toggle('selected', selected);
        }
        ui.$ ('petPause').textContent = mode === 'paused' ? '继续': '暂停';
        actor.dataset.follow = String(mode === 'follow');
    };
    const baseAction = () => busy() ? 'work': mode === 'rest' ? 'rest': 'idle';
    const schedule = () => {
        clearTimeout(roamTimer);
        if (!animated() || drag || playing || busy() || mode !== 'roam' || actor.matches(':focus')) return;
        roamTimer = setTimeout(() => {
            if (!animated() || drag || busy() || actor.matches(':focus')) return;
            if (Math.random() < .28) {
                setAction(Math.random() < .5 ? 'wave': 'rest');
                actionTimer = setTimeout(() => {
                    setAction(baseAction());
                    schedule();
                }, 2300);
            } else {
                const max = bounds();
                setAction('walk');
                position(Math.random() * max.x, max.y * .65);
                actionTimer = setTimeout(() => {
                    setAction(baseAction());
                    schedule();
                }, 1800);
            }
        }, 4000 + Math.random() * 3500);
    };
    const resume = () => {
        setAction(baseAction());
        controls();
        schedule();
    };
    const react = (kind = 'wave', text = '♪') => {
        if (!visible() || drag) return;
        stop();
        announce(text);
        setAction(kind);
        actionTimer = setTimeout(resume, 950);
    };
    const cancelDrag = () => {
        if (!drag) return;
        const id = drag.id;
        drag = null;
        if (actor.hasPointerCapture(id)) actor.releasePointerCapture(id);
        actor.style.removeProperty('--k-pet-drag-tilt');
    };
    const refresh = () => {
        if (destroyed) return;
        if (!visible()) {
            cancelDrag();
            stop();
            clearTimeout(bubbleTimer);
            bubble.hidden = true;
            artwork.stop();
            controls();
            return;
        }
        if (!initialized) {
            const max = bounds();
            x = max.x * .4;
            y = max.y * .65;
            initialized = true;
        }
        stop();
        position(x, y);
        if (drag) {
            setAction('drag');
            controls();
        } else resume();
    };
    const play = () => {
        if (!animated() || busy() || drag) return;
        stop();
        playing = true;
        announce('来玩吧 ♪');
        let remaining = 4;
        const chase = () => {
            if (!visible() || !animated() || busy() || !playing) {
                stop();
                resume();
                return;
            }
            if (!remaining --) {
                playing = false;
                ball.hidden = true;
                react('hop', '接住啦！');
                return;
            }
            const max = bounds(), target = Math.random() * max.x;
            ball.hidden = false;
            ball.style.left = clamp(target + actor.offsetWidth / 2, Math.max(0, stage.clientWidth - 18)) + 'px';
            ball.style.top = Math.max(0, stage.clientHeight - 23) + 'px';
            setAction('walk');
            position(target, max.y);
            actionTimer = setTimeout(chase, 1800);
        };
        chase();
    };
    ui.$ ('petPat').addEventListener('click', () => react('wave', '嘿嘿 ♪'), options);
    ui.$ ('petPlay').addEventListener('click', play, options);
    ball.addEventListener('click', play, options);
    ui.$ ('petRest').addEventListener('click', () => {
        if (busy()) return;
        stop();
        mode = mode === 'rest' ? 'roam': 'rest';
        announce(mode === 'rest' ? '歇一会儿…': '醒啦 ♪');
        resume();
    }, options);
    ui.$ ('petFollow').addEventListener('click', () => {
        if (busy() || !animated()) return;
        stop();
        mode = mode === 'follow' ? 'roam': 'follow';
        announce(mode === 'follow' ? '指给我看 ♪': '自由活动');
        resume();
    }, options);
    ui.$ ('petPause').addEventListener('click', () => {
        stop();
        mode = mode === 'paused' ? 'roam': 'paused';
        resume();
    }, options);
    ui.$ ('petHome').addEventListener('click', () => {
        cancelDrag();
        stop();
        mode = 'roam';
        const max = bounds();
        position(max.x * .4, max.y * .65);
        resume();
    }, options);
    stage.addEventListener('pointermove', event => {
        if (mode !== 'follow' || busy() || drag || playing || !animated() || Date.now() - lastFollow < 70) return;
        lastFollow = Date.now();
        const rect = stage.getBoundingClientRect();
        if (action !== 'walk') setAction('walk');
        position(event.clientX - rect.left - actor.offsetWidth / 2, event.clientY - rect.top - actor.offsetHeight / 2);
    }, options);
    stage.addEventListener('pointerleave', () => {
        if (mode === 'follow' && !drag) {
            stop();
            resume();
        }
    }, options);
    actor.addEventListener('pointerdown', event => {
        if (event.button !== 0 || !visible() || drag) return;
        stop();
        drag = { id: event.pointerId, clientX: event.clientX, clientY: event.clientY, x, y, moved: false };
        suppressClick = false;
        setAction('drag');
        actor.setPointerCapture(event.pointerId);
    }, options);
    actor.addEventListener('pointermove', event => {
        if (!drag || event.pointerId !== drag.id) return;
        const dx = event.clientX - drag.clientX, dy = event.clientY - drag.clientY;
        if (Math.hypot(dx, dy) > 4) drag.moved = true;
        actor.style.setProperty('--k-pet-drag-tilt', Math.max(- 8, Math.min(8, dx / 9)) + 'deg');
        position(drag.x + dx, drag.y + dy);
    }, options);
    actor.addEventListener('pointerup', event => {
        if (!drag || event.pointerId !== drag.id) return;
        suppressClick = drag.moved;
        cancelDrag();
        if (suppressClick) react('hop', '落地 ♪');
        else resume();
    }, options);
    actor.addEventListener('lostpointercapture', () => {
        if (drag) {
            drag = null;
            resume();
        }
    }, options);
    actor.addEventListener('pointercancel', () => {
        suppressClick = true;
        cancelDrag();
        resume();
    }, options);
    actor.addEventListener('click', event => {
        if (suppressClick && event.detail !== 0) {
            suppressClick = false;
            return;
        }
        react();
    }, options);
    actor.addEventListener('keydown', event => {
        const step = event.shiftKey ? 20: 8;
        const directions = { ArrowLeft: [- step, 0], ArrowRight: [step, 0], ArrowUp: [0, - step], ArrowDown: [0, step] };
        if (!directions[event.key]) return;
        event.preventDefault();
        stop();
        position(x + directions[event.key][0], y + directions[event.key][1]);
        resume();
    }, options);
    actor.addEventListener('focus', () => {
        if (!drag) {
            stop();
            setAction(baseAction());
        }
    }, options);
    actor.addEventListener('blur', schedule, options);
    document.addEventListener('visibilitychange', refresh, options);
    document.addEventListener('karolina:appearance', refresh, options);
    const resize = new ResizeObserver(refresh);
    resize.observe(stage);
    resize.observe(actor);
    const motion = new MutationObserver(refresh);
    motion.observe(document.documentElement, { attributes: true, attributeFilter: ['data-motion'] });
    const dispose = () => {
        destroyed = true;
        cancelDrag();
        stop();
        clearTimeout(bubbleTimer);
        artwork.stop();
        events.abort();
        resize.disconnect();
        motion.disconnect();
    };
    window.addEventListener('pagehide', dispose, { once: true, signal: events.signal });
    return {
        apply(assets, preferences) {
            cancelDrag();
            enabled = preferences.mascotVisible;
            artwork.apply(assets);
            const ground = ui.$ ('petGround');
            ground.hidden = !assets.desktopPetGround;
            if (!ground.hidden) ground.src = assets.desktopPetGround;
            refresh();
        },
        setEmotion(value) {
            if (emotion === value) return;
            emotion = value;
            host.dataset.emotion = value;
            refresh();
            if (value === 'happy') react('hop', '完成啦 ♪');
            if (value === 'error') announce('陪你一起想办法');
        },
        dispose
    };
}
