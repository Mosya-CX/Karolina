const vertexSource = 'attribute vec2 aPosition;void main(){gl_Position=vec4(aPosition,0.0,1.0);}';
/** Optional WebGL layer with a bounded framebuffer, 30 fps cap, and explicit lifecycle. */
export function createEffects(toast) {
    const canvas = document.getElementById('stageEffects');
    let gl = null, program = null, buffer = null, uniforms = { };
    let config = { mode: 'static', motion: 'off' }, generation = 0, frame = 0, lastDraw = 0, epoch = performance.now();
    let disposed = false, lost = false, source = '', status = '静态背景';
    const pointer = [.5, .5];
    let palette = { pink: [.93, .5, .81], cyan: [.33, .86, .91], purple: [.64, .54, .97] };
    const notify = value => {
        status = value;
        canvas.dataset.status = value;
        document.dispatchEvent(new CustomEvent('karolina:effects', { detail: { status: value } }));
    };
    const stop = () => {
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
    };
    const release = () => {
        stop();
        if (gl && !lost) {
            if (program) gl.deleteProgram(program);
            if (buffer) gl.deleteBuffer(buffer);
        }
        program = null;
        buffer = null;
        uniforms = { };
    };
    const shader = (type, code) => {
        const object = gl.createShader(type);
        gl.shaderSource(object, code);
        gl.compileShader(object);
        if (!gl.getShaderParameter(object, gl.COMPILE_STATUS)) {
            const message = gl.getShaderInfoLog(object);
            gl.deleteShader(object);
            throw new Error('Shader 编译失败：' + message);
        }
        return object;
    };
    const compile = code => {
        release();
        const vertex = shader(gl.VERTEX_SHADER, vertexSource);
        let fragment;
        try {
            fragment = shader(gl.FRAGMENT_SHADER, code);
            program = gl.createProgram();
            gl.attachShader(program, vertex);
            gl.attachShader(program, fragment);
            gl.linkProgram(program);
            if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error('Shader 链接失败：' + gl.getProgramInfoLog(program));
        } finally {
            gl.deleteShader(vertex);
            if (fragment) gl.deleteShader(fragment);
        }
        gl.useProgram(program);
        buffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([- 1, - 1, 1, - 1, - 1, 1, - 1, 1, 1, - 1, 1, 1]), gl.STATIC_DRAW);
        const position = gl.getAttribLocation(program, 'aPosition');
        if (position < 0) throw new Error('Shader 缺少 aPosition');
        gl.enableVertexAttribArray(position);
        gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
        for (const name of['uTime', 'uResolution', 'uPointer', 'uIntensity', 'uPink', 'uCyan', 'uPurple']) uniforms[name] = gl.getUniformLocation(program, name);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
        resize();
    };
    const resize = () => {
        const rect = canvas.getBoundingClientRect();
        const ratio = Math.min(devicePixelRatio || 1, 1.5);
        const budget = Math.min(1, Math.sqrt(1800000 / Math.max(1, rect.width * rect.height * ratio * ratio)));
        const width = Math.max(1, Math.round(rect.width * ratio * budget)), height = Math.max(1, Math.round(rect.height * ratio * budget));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
        if (gl && !lost) gl.viewport(0, 0, width, height);
    };
    const color = (name, fallback) => {
        const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        if (/^#[0-9a-f]{6}$/i.test(value)) return[1, 3, 5].map(i => parseInt(value.slice(i, i + 2), 16) / 255);
        return fallback;
    };
    const draw = now => {
        frame = 0;
        if (disposed || lost || !program || document.hidden || config.mode === 'static') return;
        const animate = config.motion === 'full';
        if (now - lastDraw >= 32 || !animate) {
            lastDraw = now;
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
            gl.useProgram(program);
            gl.uniform1f(uniforms.uTime, animate ? (now - epoch) / 1000: 0);
            gl.uniform2f(uniforms.uResolution, canvas.width, canvas.height);
            gl.uniform2fv(uniforms.uPointer, pointer);
            gl.uniform1f(uniforms.uIntensity, config.intensity ?? .42);
            gl.uniform3fv(uniforms.uPink, palette.pink);
            gl.uniform3fv(uniforms.uCyan, palette.cyan);
            gl.uniform3fv(uniforms.uPurple, palette.purple);
            gl.drawArrays(gl.TRIANGLES, 0, 6);
        }
        if (animate) frame = requestAnimationFrame(draw);
    };
    const resume = () => {
        stop();
        if (!disposed && !lost && !document.hidden && program && config.mode !== 'static') frame = requestAnimationFrame(draw);
    };
    const configure = async next => {
        config = { ... next };
        palette = { pink: color('--k-pink', [.93, .5, .81]), cyan: color('--k-cyan', [.33, .86, .91]), purple: color('--k-purple', [.64, .54, .97]) };
        const request = ++ generation;
        stop();
        if (next.mode === 'static' || next.motion === 'off' || !next.fragment) {
            canvas.hidden = true;
            notify('静态背景');
            return;
        }
        if (lost) {
            canvas.hidden = true;
            notify('GPU 上下文暂不可用，使用静态背景');
            return;
        }
        try {
            gl ||= canvas.getContext('webgl', { alpha: true, antialias: false, premultipliedAlpha: false, powerPreference: 'low-power' });
            if (!gl) throw new Error('当前环境不支持 WebGL');
            const response = await fetch(next.fragment, { cache: 'no-cache' });
            if (!response.ok) throw new Error('Shader 文件读取失败');
            const code = await response.text();
            if (request !== generation || disposed) return;
            if (source !== code || !program) {
                compile(code);
                source = code;
            }
            if (request !== generation || disposed) return;
            canvas.hidden = false;
            notify(next.motion === 'full' ? 'Shader · 动态夜空': 'Shader · 静态采样');
            resume();
        } catch (error) {
            if (request !== generation || disposed) return;
            release();
            canvas.hidden = true;
            notify('静态回退 · ' + error.message);
            toast('场景特效未启用：' + error.message + '。背景仍可正常显示。');
        }
    };
    const visible = () => {
        if (document.hidden) stop();
        else resume();
    };
    const onPointer = event => {
        if (config.motion !== 'full') return;
        const rect = canvas.getBoundingClientRect();
        pointer[0] = (event.clientX - rect.left) / Math.max(1, rect.width);
        pointer[1] = 1 - (event.clientY - rect.top) / Math.max(1, rect.height);
    };
    const contextLost = event => {
        event.preventDefault();
        lost = true;
        stop();
        canvas.hidden = true;
        notify('GPU 上下文已丢失，使用静态背景');
    };
    const contextRestored = () => {
        lost = false;
        program = null;
        buffer = null;
        source = '';
        configure(config);
    };
    const observer = new ResizeObserver(() => {
        resize();
        resume();
    });
    observer.observe(canvas.parentElement);
    document.addEventListener('visibilitychange', visible);
    document.addEventListener('pointermove', onPointer, { passive: true });
    canvas.addEventListener('webglcontextlost', contextLost);
    canvas.addEventListener('webglcontextrestored', contextRestored);
    return {
        configure,
        setMotion(motion) {
            config.motion = motion;
            configure(config);
        },
        get status() {
            return status;
        },
        dispose() {
            disposed = true;
            ++ generation;
            release();
            observer.disconnect();
            document.removeEventListener('visibilitychange', visible);
            document.removeEventListener('pointermove', onPointer);
            canvas.removeEventListener('webglcontextlost', contextLost);
            canvas.removeEventListener('webglcontextrestored', contextRestored);
        }
    };
}
