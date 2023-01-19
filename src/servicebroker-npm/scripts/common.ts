import * as fs from 'fs';

export function mkdirIfNotExist(path: string): void {
    try {
        fs.accessSync(path);
    } catch (err) {
        fs.mkdirSync(path);
    }
}
