import * as c from './common';
import * as fs from 'fs';
import * as path from 'path';

var glob = require('glob');

let rootFileGlobs = [
    { glob: 'package.json', root: './' },
    { glob: 'LICENSE.txt', root: './' },
    { glob: 'ThirdPartyNotices.txt', root: './' },
    { glob: './js/*.js', root: './' },
    { glob: './js/*.js.map', root: './' },
    { glob: './js/*.d.ts', root: './' },
    { glob: './js/*.json', root: './' },
];

let excludeFileGlobs = ['./*.metadata.json', './*.deps.json'];

let packageDir = './js/package';
c.mkdirIfNotExist(packageDir);

// Copy all files that should be included in the NPM package to a "package" directory.
rootFileGlobs.forEach((rootFiles) => {
    const files = glob.sync(rootFiles.glob);
    files.forEach((file: string) => {
        let destinationDir = path.join(packageDir, rootFiles.root);
        c.mkdirIfNotExist(destinationDir);

        fs.copyFileSync(file, path.join(destinationDir, path.basename(file)));
    });
});

// Delete all files that should be excluded from the NPM package
excludeFileGlobs.forEach((exclude) => {
    const files = glob.sync(path.join(packageDir, exclude));
    files.forEach((file: string) => {
        fs.unlinkSync(file);
    });
});
