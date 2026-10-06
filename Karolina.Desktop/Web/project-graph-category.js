export function graphCategory(node) {
    if (node.kind === 'C# 类型') return 'type';
    if (node.kind === '代码文件') return 'code';
    if (node.kind === '工程文档') return 'document';
    if (node.kind === '工程文件') return 'file';
    const extension = (node.path || '').split('.').pop().toLowerCase();
    if (extension === 'unity') return 'scene';
    if (extension === 'prefab') return 'prefab';
    if (['glb', 'gltf', 'fbx', 'obj', 'anim', 'controller'].includes(extension)) return 'art';
    return 'resource';
}
