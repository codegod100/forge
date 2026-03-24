let
  nandi = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPsBDVEb9Kl3JfyOQRJE8jPtIXPjfnmv4oFGKVxvMwnH nandi@nixos";
in
{
  "secrets/admin-password.age".publicKeys = [ nandi ];
}
