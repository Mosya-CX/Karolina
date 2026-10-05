export function renderFileExplanation(target,data,ui,requestExplanation) {
    const section=document.createElement('section');section.className='file-explanation';
    const explanation=data.explanation || data.file.explanation;
    const title=document.createElement('h3');title.textContent=data.file.origin==='agent'?'Agent对这个文件做了什么':data.file.origin==='human'?'人工提供的文件 · 不参与审批':'文件变更说明 · 来源待确认';section.append(title);
    if(data.file.origin==='human'){const message=document.createElement('p');message.textContent='已确认为人工提供或修改，不属于Agent审批。'+data.file.originEvidence;section.append(message);target.append(section);return;}
    if(!explanation || explanation.fingerprint!==data.file.fingerprint) {
        const message=document.createElement('p');message.textContent=data.file.origin==='agent'?'这个版本尚未记录Agent的操作说明。需要说明具体做了什么、原因及验证结果。':'实际修改来源尚待确认。可以补充冻结变化分析，但这不会认领文件作者或批准文件。';
        const button=document.createElement('button');button.textContent='让Agent补充文件说明';button.onclick=requestExplanation;section.append(message,button);
    } else {
        const summary=document.createElement('strong');summary.className='file-explanation-summary';summary.textContent=explanation.summary;section.append(summary);
        for(const [caption,text] of [['具体改动',explanation.whatChanged],['原因与需求',explanation.why],['验证与限制',explanation.verification]]) {
            if(!text)continue;const block=document.createElement('div'),heading=document.createElement('h4'),body=document.createElement('p');heading.textContent=caption;body.textContent=text;block.append(heading,body);section.append(block);
        }
        const source=document.createElement('small');source.textContent='说明来源：'+explanation.source;section.append(source);
    }
    target.append(section);
}
