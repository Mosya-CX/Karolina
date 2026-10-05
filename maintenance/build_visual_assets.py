"""Author the project's editable vector artwork and built-in appearance manifests. No runtime dependency."""
from pathlib import Path
import json

WEB = Path(__file__).resolve().parents[1] / 'Karolina.Desktop/Web'
THEME = WEB / 'themes/karolina'
ICONS = THEME / 'icons'
ICONS.mkdir(parents=True, exist_ok=True)

# Round-ended line drawings with semantic names; each resource is independently replaceable.
paths = {
 'chat':'<path d="M5 5h14v10H11l-5 4v-4H3V7z"/><path d="M8 9h.1M12 9h.1M16 9h.1"/>',
 'chat-add':'<path d="M4 4h12v11H9l-5 4V4"/><path d="M17 8h6m-3-3v6"/>',
 'document':'<path d="M6 3h8l4 4v14H6zM14 3v5h4M9 12h6M9 16h6"/>',
 'plan':'<path d="M6 6h11v6H7v6h10"/><circle cx="6" cy="6" r="2"/><circle cx="7" cy="12" r="2"/><circle cx="17" cy="18" r="2"/>',
 'book':'<path d="M12 6c-3-3-7-3-9-2v15c4-1 7 0 9 2 2-2 5-3 9-2V4c-2-1-6-1-9 2v15M6 8h3M15 8h3"/>',
 'approval':'<path d="M4 4h7v16H4zM14 4h6v8M14 17l3 3 5-6"/>',
 'tools':'<path d="M7 7h4l6 10M7 17h4l6-10"/><circle cx="5" cy="7" r="2"/><circle cx="5" cy="17" r="2"/><path d="M17 4h4v6h-4zM17 14h4v6h-4z"/>',
 'theme':'<path d="M16 4a9 9 0 1 0 4 13 7 7 0 0 1-4-13z"/><path d="M5 3v2M3 4h4"/>',
 'search':'<circle cx="10" cy="10" r="6"/><path d="m15 15 6 6"/>',
 'reference':'<path d="M7 3h13v15H7zM4 7v14h12M10 7h6M10 11h6"/>',
 'attach':'<path d="m8 17 8-8a3 3 0 0 0-4-4L3 14a5 5 0 0 0 7 7L21 10a6 6 0 0 0-8-8"/>',
 'send':'<path d="m3 11 18-8-8 18-2-8zM11 13 21 3"/>',
 'stop':'<rect x="6" y="6" width="12" height="12" rx="1"/>',
 'execute':'<path d="m7 5-4 7 4 7M16 6l6 6-6 6zM12 4 10 20"/>',
 'read':'<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
 'edit':'<path d="m4 16-1 5 5-1L21 7l-4-4zM14 6l4 4"/>',
 'save':'<path d="M4 3h14l3 3v15H3V4zM7 3v6h9V3M7 21v-7h10v7"/>',
 'info':'<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7h.01"/>',
 'timeline':'<path d="M4 10a8 8 0 1 1 1 8M4 4v6h6M12 7v5l4 2"/>',
 'compare':'<path d="M3 4h7v16H3zM14 4h7v16h-7M6 9h1M6 13h1M17 9h1M17 13h1M10 12h4"/>',
 'comment':'<path d="M3 4h18v12H9l-5 4v-4H3z"/><circle cx="16" cy="8" r="1.5"/>',
 'check':'<path d="m4 12 5 5L20 6"/>',
 'return':'<path d="m9 5-7 7 7 7M2 12h12a7 7 0 0 1 7 7"/>',
 'refresh':'<path d="M20 8a8 8 0 0 0-14-3L3 8M3 3v5h5M4 16a8 8 0 0 0 14 3l3-3M21 21v-5h-5"/>',
 'meta':'<path d="M7 3h12v14H7zM3 7v14h12M9 10s2-3 4-3 4 3 4 3-2 3-4 3-4-3-4-3z"/>',
 'add':'<path d="M12 4v16M4 12h16"/>',
 'play':'<path d="m7 3 14 9-14 9z"/>',
 'record':'<circle cx="12" cy="12" r="9"/><path d="M12 6v7l4 2"/>',
 'unity':'<path d="m12 2 9 5v10l-9 5-9-5V7zM3 7l9 5 9-5M12 12v10"/>',
 'settings':'<path d="m9 3-1 3-3 1v4l-2 1 2 2v4l3 1 1 2h5l1-2 4-1v-4l2-2-2-1V7l-4-1-1-3z"/><circle cx="11.5" cy="12" r="3"/>',
 'close':'<path d="m5 5 14 14M19 5 5 19"/>',
 'menu':'<path d="M4 6h16M4 12h12M4 18h16"/>',
 'clear':'<path d="m6 6 12 12M18 6 6 18"/>',
 'chevron':'<path d="m8 5 7 7-7 7"/>',
 'download':'<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>',
 'upload':'<path d="M12 16V4m-5 5 5-5 5 5M4 17v4h16v-4"/>',
 'reset':'<path d="M4 9a8 8 0 1 1 0 6M4 3v6h6"/>',
 'code':'<path d="m8 6-6 6 6 6m8-12 6 6-6 6M14 3 10 21"/>',
 'controller':'<circle cx="8" cy="12" r="4"/><path d="M8 8v8M4 12h8"/><circle cx="18" cy="8" r="1"/><circle cx="21" cy="12" r="1"/><circle cx="18" cy="16" r="1"/>',
}

for name, drawing in paths.items():
    (ICONS / f'{name}.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#e1e9ff" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round">{drawing}</svg>\n', encoding='utf-8')

# Project-native B1 interpretation: K spine, smaller widely forked left wings, larger swept right wings.
triangles = [
 ('22,25 31,34 28,105',.65), ('31,34 39,47 28,105',.35), ('28,105 32,164 39,47',.45),
 ('28,105 72,55 137,9',.22), ('28,105 137,9 120,56',.24), ('28,105 120,56 91,80',.32),
 ('28,105 91,80 74,103',.28), ('28,105 79,115 120,153',.27), ('28,105 120,153 65,144',.20),
 ('28,105 65,144 38,165',.32), ('28,105 19,69 0,37',.17), ('28,105 0,37 15,47',.22),
 ('28,105 7,134 0,165',.17), ('28,105 0,165 18,146',.25)
]
brand = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="-15 -8 172 189"><defs><linearGradient id="wing" x2=".95" y2=".18"><stop stop-color="#69e7ef"/><stop offset=".55" stop-color="#9783ff"/><stop offset="1" stop-color="#f696e7"/></linearGradient><filter id="bloom" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="2.4"/></filter></defs><g fill="url(#wing)" stroke="#a9c7ff" stroke-width=".65">'
brand += ''.join(f'<polygon points="{p}" fill-opacity="{opacity}" stroke-opacity=".45"/>' for p,opacity in triangles)
brand += '</g><path d="M28 105 137 9Q116 56 74 103M28 105Q93 108 120 153L65 144 38 165M22 25l9 9-3 71 4 59" fill="none" stroke="#72dfff" opacity=".48"/><path d="M28 105 137 9M28 105 120 153M22 25l9 9-3 71" fill="none" stroke="#b5c8ff" filter="url(#bloom)"/>'
brand += ''.join(f'<circle cx="{x}" cy="{y}" r=".9" fill="#cdf4ff" opacity="{o}"/>' for x,y,o in [(28,105,.8),(137,9,.8),(65,144,.4),(74,103,.3),(120,56,.4),(22,25,.4),(32,164,.3),(110,29,.2),(129,1,.2),(142,18,.3)])
brand += '</svg>'
(THEME/'assets/k-butterfly.svg').write_text(brand,encoding='utf-8')

tokens = {
 '--k-font-ui':'"HarmonyOS Sans SC", "Microsoft YaHei UI", system-ui, sans-serif',
 '--k-font-code':'"Cascadia Code", Consolas, monospace',
 '--k-bg':'#080d21', '--k-panel':'#0d1530', '--k-panel-raised':'#14203d',
 '--k-panel-glass':'#101a31e8', '--k-text':'#e8edff', '--k-muted':'#9ba8c6',
 '--k-line':'#8ca2db35', '--k-pink':'#ed7fce', '--k-cyan':'#55dce7', '--k-purple':'#a48af7', '--k-blue':'#69a9f6',
 '--k-accent':'#b1a0ff', '--k-accent-soft':'#8870e52b', '--k-success':'#70dbc4', '--k-danger':'#f28faa',
 '--k-code':'#080e23', '--k-added':'#4dd9ae18', '--k-deleted':'#ec719925',
 '--k-button-bg':'#10203b9e', '--k-button-border':'#9baed976', '--k-button-hover-bg':'#17324a',
 '--k-button-cut':'10px', '--k-button-node-size':'4px', '--k-button-node-x':'87%', '--k-button-node-y':'8px',
 '--k-button-indicator-size':'7px', '--k-button-indicator-y':'3px', '--k-button-min-height':'34px',
 '--k-button-padding-x':'15px', '--k-button-padding-y':'8px', '--k-icon-size':'18px',
 '--k-rail-width':'68px', '--k-sidebar-width':'260px', '--k-radius':'10px', '--k-panel-blur':'16px',
 '--k-motion-fast':'140ms', '--k-motion-medium':'260ms', '--k-motion-slow':'600ms', '--k-motion-easing':'cubic-bezier(.2,.8,.2,1)',
 '--k-focus':'#79e7ed', '--k-scene-position':'center center', '--k-scene-filter':'saturate(.86)',
 '--k-space-unit':'4px', '--k-mascot-height':'148px', '--k-body-font-size':'14px'
}
light = {
 '--k-bg':'#edf0fa', '--k-panel':'#f7f8ff', '--k-panel-raised':'#ffffff', '--k-panel-glass':'#f7f8fff2',
 '--k-text':'#29304e', '--k-muted':'#616b8b', '--k-line':'#606c992a', '--k-accent':'#7554c0', '--k-accent-soft':'#ae97e320',
 '--k-code':'#e6eaf5','--k-button-bg':'#edf0faf0','--k-button-hover-bg':'#dde6f9', '--k-button-border':'#6f75a78c',
 '--k-success':'#08796d','--k-danger':'#ab395e','--k-added':'#59c4ae25','--k-deleted':'#eb789d25','--k-focus':'#087f99'
}
manifest={'schemaVersion':1,'id':'karolina','name':'Karolina · 深空蝶翼','description':'粉 / 青 / 紫，深空工作台与不对称面片。','tokens':tokens,'colorModes':{'light':light},'assets':{'background':'assets/workbench-night.png','mascot':'assets/karolina-mascot.png','brand':'assets/k-butterfly.svg'},'icons':{name:f'icons/{name}.svg' for name in paths},'components':{},'styles':[],'fonts':[{'family':'HarmonyOS Sans SC','source':'fonts/HarmonyOS_Sans_SC.ttf','weight':'100 900','style':'normal'}],'effect':{'fragment':'effects/night.frag','intensity':.42}}
manifest['icons']['codex']='icons/codex.png'
(THEME/'theme.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
minimal=WEB/'themes/paper';minimal.mkdir(parents=True,exist_ok=True)
(minimal/'theme.json').write_text(json.dumps({'schemaVersion':1,'id':'paper','name':'Karolina · 轻阅读','description':'保留品牌，降低装饰与背景比重；也是部分覆盖主题的示例。','tokens':{'--k-button-node-size':'2px','--k-panel-blur':'8px','--k-mascot-height':'116px'},'colorModes':{'light':light},'components':{'button-frame':'button-frame.css'},'effect':{'intensity':.12}},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(minimal/'button-frame.css').write_text('.k-button-frame .k-frame-leading, .k-button-frame .k-frame-trailing { opacity: .32; }\n.k-corner-node { opacity: .4; }\n',encoding='utf-8')
print(f'Authored {len(paths)} individual icons, brand and two theme manifests')
