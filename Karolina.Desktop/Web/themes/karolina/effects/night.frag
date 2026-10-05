precision mediump float;
uniform vec2 uResolution;
uniform float uTime;
uniform vec2 uPointer;
uniform float uIntensity;
uniform vec3 uPink;
uniform vec3 uCyan;
uniform vec3 uPurple;

float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float stars(vec2 uv,float scale,float depth) {
    vec2 cell=floor(uv*scale),local=fract(uv*scale);
    float seed=hash(cell+depth);
    vec2 point=vec2(hash(cell+1.7),hash(cell+4.3))*.7+.15;
    float shape=1.0-smoothstep(.01,.08,length(local-point));
    return shape*step(.982,seed)*(.55+.25*sin(uTime*.5+seed*50.0));
}
float meteor(vec2 p,float layer) {
    float period=19.0+layer*11.0;
    float cycle=floor(uTime/period+layer*.29);
    float phase=fract(uTime/period+layer*.29);
    float active=smoothstep(.74,.77,phase)*(1.0-smoothstep(.85,.90,phase));
    vec2 head=vec2(.95-hash(vec2(cycle,layer))*.7,.95-layer*.08);
    head+=vec2(-.36,-.18)*clamp((phase-.75)*8.0,0.0,1.0);
    vec2 delta=p-head;
    vec2 direction=normalize(vec2(2.0,1.0));
    float along=dot(delta,direction);
    float distance=abs(dot(delta,vec2(-direction.y,direction.x)));
    float streak=exp(-distance*(480.0+layer*100.0))*(1.0-smoothstep(0.0,.17,along))*step(0.0,along);
    float tip=exp(-length(delta)*500.0);
    return (streak*.45+tip)*active;
}
void main() {
    vec2 uv=gl_FragCoord.xy/max(uResolution,vec2(1.0));
    vec2 p=uv+((uPointer-.5)*.003);
    p.x*=uResolution.x/max(1.0,uResolution.y);
    float sky=smoothstep(.42,.68,uv.y);
    float drift=sin(p.x*3.0+p.y*2.0+uTime*.027)*.5+.5;
    float haze=exp(-pow((uv.y-.78)*3.5,2.0))*.055*drift;
    float spark=stars(p,62.0,1.0)*.22+stars(p,35.0,3.0)*.15;
    float trails=meteor(p,0.0)+meteor(p,1.0)*.55+meteor(p,2.0)*.3;
    vec3 color=mix(uPurple,uCyan,.25+uv.y*.5);
    color=mix(color,uPink,clamp(trails*.7,0.0,.65));
    float alpha=clamp((haze+spark+trails*.65)*sky*uIntensity,0.0,.6);
    gl_FragColor=vec4(color,alpha);
}
