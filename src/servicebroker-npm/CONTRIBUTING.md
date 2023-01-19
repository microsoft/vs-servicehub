# Development

As a first time setup, run the `init.ps1` script in this directory.
Run `yarn` after any updates made to package.json.

We use [gulp](https://gulpjs.com/) for development, so specific script implementation can be found in gulpfile.js. With this, you can

-   Build: `gulp build`
-   Test: `gulp test` OR `yarn test`.
-   Create a new package: `gulp package`
-   Or all of the above: `gulp`

## Testing

Run or debug a subset of tests using VS Code's "Test" panel. Tests will show up after you install the VS Code extensions recommended for this folder.
