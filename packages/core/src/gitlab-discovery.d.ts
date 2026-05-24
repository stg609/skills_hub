export type GitLabTreeItem = {
  type: string;
  path: string;
};

export declare function discoverSkillFiles(tree: GitLabTreeItem[]): Array<{
  skillDir: string;
  skillFilePath: string;
}>;
