export declare function buildPackageEntries(input: {
  skillDir: string;
  files: Array<{ path: string; size?: number }>;
  maxFiles: number;
  maxBytes: number;
}): Array<{
  sourcePath: string;
  archivePath: string;
}>;
